using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace NabconsIntranet.Infrastructure;

public sealed record BackupInfo(string FileName, string FullPath, long SizeBytes, DateTime CreatedUtc, string? Sha256);

/// <summary>
/// Creates a single ZIP containing a consistent copy of the database (SQLite VACUUM INTO),
/// every stored document, the configuration file and a CSV export of the audit log.
/// A matching .sha256 file is written next to it so a restore can be verified.
/// </summary>
public sealed class BackupService
{
    private readonly IntranetOptions _o;
    private readonly ILogger<BackupService> _log;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public const string Prefix = "NABCONS-Intranet-Backup-";

    public BackupService(IntranetOptions o, ILogger<BackupService> log)
    {
        _o = o;
        _log = log;
    }

    public BackupInfo Create(AuditService? audit, string actor)
    {
        Gate.Wait();
        try
        {
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var work = Path.Combine(_o.BackupPath, "_work-" + stamp);
            Directory.CreateDirectory(work);
            var zipPath = Path.Combine(_o.BackupPath, Prefix + stamp + ".zip");
            try
            {
                var dbCopy = Path.Combine(work, "intranet.db");
                using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _o.DatabasePath, Pooling = false }.ToString()))
                {
                    c.Open();
                    using var cmd = c.CreateCommand();
                    cmd.CommandText = "VACUUM INTO @p";
                    cmd.Parameters.AddWithValue("@p", dbCopy);
                    cmd.ExecuteNonQuery();
                }

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    zip.CreateEntryFromFile(dbCopy, "data/intranet.db", CompressionLevel.Optimal);
                    if (Directory.Exists(_o.UploadsPath))
                    {
                        foreach (var f in Directory.EnumerateFiles(_o.UploadsPath, "*", SearchOption.AllDirectories))
                            zip.CreateEntryFromFile(f, "data/uploads/" + Path.GetRelativePath(_o.UploadsPath, f).Replace('\\', '/'), CompressionLevel.Fastest);
                    }
                    if (Directory.Exists(_o.DocumentsPath))
                    {
                        foreach (var f in Directory.EnumerateFiles(_o.DocumentsPath, "*", SearchOption.AllDirectories))
                            zip.CreateEntryFromFile(f, "documents/" + Path.GetRelativePath(_o.DocumentsPath, f).Replace('\\', '/'), CompressionLevel.Fastest);
                    }
                    var settings = Path.Combine(_o.ContentRoot, "appsettings.json");
                    if (File.Exists(settings)) zip.CreateEntryFromFile(settings, "config/appsettings.json");
                    if (audit != null)
                    {
                        var entry = zip.CreateEntry("audit/audit-log-export.csv");
                        using var w = new StreamWriter(entry.Open());
                        audit.ExportCsv(w);
                    }
                    var readme = zip.CreateEntry("RESTORE-README.txt");
                    using (var w = new StreamWriter(readme.Open()))
                        w.Write($"NABCONS Intranet backup created {DateTime.Now:dd MMM yyyy HH:mm} by {actor}.\r\n" +
                                "To restore: stop the intranet, then run Restore-Backup.bat and choose this file.\r\n");
                }
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }

            var sha = HashFile(zipPath);
            File.WriteAllText(zipPath + ".sha256", $"{sha}  {Path.GetFileName(zipPath)}\r\n");

            if (!string.IsNullOrWhiteSpace(_o.SecondaryBackupFolder))
            {
                try
                {
                    Directory.CreateDirectory(_o.SecondaryBackupFolder);
                    File.Copy(zipPath, Path.Combine(_o.SecondaryBackupFolder, Path.GetFileName(zipPath)), true);
                    File.Copy(zipPath + ".sha256", Path.Combine(_o.SecondaryBackupFolder, Path.GetFileName(zipPath) + ".sha256"), true);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Copy to secondary backup folder {Folder} failed", _o.SecondaryBackupFolder);
                    audit?.Log("Backup.SecondaryCopyFailed", "Backup", Path.GetFileName(zipPath), ex.Message, actor);
                }
            }

            ApplyRetention();
            var info = new FileInfo(zipPath);
            audit?.Log("Backup.Created", "Backup", info.Name, $"{info.Length} bytes, SHA-256 {sha}", actor);
            return new BackupInfo(info.Name, info.FullName, info.Length, info.CreationTimeUtc, sha);
        }
        finally
        {
            Gate.Release();
        }
    }

    public List<BackupInfo> List()
    {
        if (!Directory.Exists(_o.BackupPath)) return new();
        return Directory.EnumerateFiles(_o.BackupPath, Prefix + "*.zip")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Name)
            .Select(f =>
            {
                var shaFile = f.FullName + ".sha256";
                var sha = File.Exists(shaFile) ? File.ReadAllText(shaFile).Split(' ', 2)[0].Trim() : null;
                return new BackupInfo(f.Name, f.FullName, f.Length, f.CreationTimeUtc, sha);
            })
            .ToList();
    }

    private void ApplyRetention()
    {
        if (_o.BackupRetentionDays <= 0) return;
        var cutoff = DateTime.UtcNow.AddDays(-_o.BackupRetentionDays);
        foreach (var f in Directory.EnumerateFiles(_o.BackupPath, Prefix + "*.zip").Select(p => new FileInfo(p)))
        {
            if (f.CreationTimeUtc >= cutoff) continue;
            try
            {
                f.Delete();
                if (File.Exists(f.FullName + ".sha256")) File.Delete(f.FullName + ".sha256");
            }
            catch (Exception ex) { _log.LogWarning(ex, "Could not delete old backup {File}", f.Name); }
        }
    }

    public static string HashFile(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s));
    }

    /// <summary>
    /// Restores a backup ZIP (run from the command line while the intranet is stopped).
    /// The current data and documents folders are moved aside first, never deleted.
    /// </summary>
    public static int Restore(IntranetOptions o, string zipPath, TextWriter output)
    {
        if (!File.Exists(zipPath)) { output.WriteLine($"Backup file not found: {zipPath}"); return 2; }
        var shaFile = zipPath + ".sha256";
        if (File.Exists(shaFile))
        {
            var expected = File.ReadAllText(shaFile).Split(' ', 2)[0].Trim();
            var actual = HashFile(zipPath);
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                output.WriteLine("CHECKSUM MISMATCH - the backup file is damaged or was altered. Restore cancelled.");
                return 3;
            }
            output.WriteLine("Checksum verified: " + actual);
        }
        else output.WriteLine("Warning: no .sha256 file found next to the backup; integrity cannot be verified.");

        using var zip = ZipFile.OpenRead(zipPath);
        if (zip.GetEntry("data/intranet.db") == null) { output.WriteLine("This ZIP is not a NABCONS Intranet backup."); return 4; }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        foreach (var dir in new[] { o.DataPath, o.DocumentsPath })
        {
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any())
            {
                var aside = dir.TrimEnd(Path.DirectorySeparatorChar) + ".before-restore-" + stamp;
                Directory.Move(dir, aside);
                output.WriteLine($"Moved current {Path.GetFileName(dir)} folder to {aside}");
            }
            Directory.CreateDirectory(dir);
        }

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string? targetRoot = null, relative = null;
            if (entry.FullName.StartsWith("data/", StringComparison.Ordinal)) { targetRoot = o.DataPath; relative = entry.FullName[5..]; }
            else if (entry.FullName.StartsWith("documents/", StringComparison.Ordinal)) { targetRoot = o.DocumentsPath; relative = entry.FullName[10..]; }
            if (targetRoot == null || relative == null) continue;
            var root = Path.GetFullPath(targetRoot) + Path.DirectorySeparatorChar;
            var dest = Path.GetFullPath(Path.Combine(targetRoot, relative));
            if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue; // zip-slip protection
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }
        output.WriteLine("Restore complete. Start the intranet again and check the Audit page -> Verify integrity.");
        return 0;
    }
}

/// <summary>Runs the daily backup at the configured local time.</summary>
public sealed class BackupScheduler(BackupService backups, AuditService audit, IntranetOptions o, ILogger<BackupScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!TimeOnly.TryParse(o.DailyBackupTime, CultureInfo.InvariantCulture, out var at))
        {
            log.LogWarning("DailyBackupTime '{Value}' is not a valid time; scheduled backups are disabled.", o.DailyBackupTime);
            return;
        }
        var marker = Path.Combine(o.BackupPath, "last-scheduled-backup.txt");
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.Now;
            var last = File.Exists(marker) ? File.ReadAllText(marker).Trim() : "";
            if (TimeOnly.FromDateTime(now) >= at && last != now.ToString("yyyy-MM-dd"))
            {
                try
                {
                    backups.Create(audit, "system (scheduled backup)");
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Scheduled backup failed");
                    try { audit.Log("Backup.Failed", "Backup", null, ex.Message, "system (scheduled backup)"); } catch { /* ignore */ }
                }
                // One attempt per day; a failure is visible on the Backup page and in the audit log.
                try { File.WriteAllText(marker, now.ToString("yyyy-MM-dd")); } catch { /* ignore */ }
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ContinueWith(_ => { });
        }
    }
}
