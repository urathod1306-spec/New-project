namespace NabconsIntranet.Infrastructure;

public sealed record HousekeepingResult(int ReadDeleted, int UnreadDeleted, List<string> Notes);

/// <summary>
/// Daily tidy-up, run by the scheduler straight after the daily backup (outside office hours).
/// Notifications are a convenience, not a record: the audit log remains the system of record.
/// Deletes run in small batches so the database is never locked for long.
/// </summary>
public sealed class HousekeepingService(Db db, NotificationOptions no, AuditService audit, IntranetOptions io, ILogger<HousekeepingService> log)
{
    private const int Batch = 5000;

    /// <summary>Extra daily steps added by modules (e-Note: ageing alerts).</summary>
    public List<Func<List<string>>> ExtraSteps { get; } = new();

    public HousekeepingResult Run(DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var readCut = now.AddDays(-Math.Max(1, no.RetainReadDays)).ToString("o");
        var unreadCut = now.AddDays(-Math.Max(1, no.RetainUnreadDays)).ToString("o");
        var read = DeleteInBatches("ReadAt IS NOT NULL AND CreatedAt < @cut", readCut);
        var unread = DeleteInBatches("ReadAt IS NULL AND CreatedAt < @cut", unreadCut);
        var notes = new List<string>();
        // keeps the query planner's statistics current as the tables grow (cheap; SQLite only re-analyses what changed)
        try { db.Exec("PRAGMA optimize;"); } catch (Exception ex) { log.LogWarning(ex, "PRAGMA optimize failed"); }
        foreach (var step in ExtraSteps.Prepend((Func<List<string>>)OrphanFiles))
        {
            try { notes.AddRange(step()); }
            catch (Exception ex) { log.LogError(ex, "Housekeeping step failed"); notes.Add("A housekeeping step failed: " + ex.Message); }
        }
        audit.Log("System.Housekeeping", "System", null,
            $"Notifications removed: {read} read (older than {no.RetainReadDays} days), {unread} unread (older than {no.RetainUnreadDays} days)" +
            (notes.Count > 0 ? "; " + string.Join("; ", notes) : ""), "system (housekeeping)");
        return new HousekeepingResult(read, unread, notes);
    }

    /// <summary>
    /// Reports (never deletes) stored documents that no record points to, e.g. after a save that failed half-way,
    /// across every module: request, circular, lead and e-Note files. Generated e-Note PDFs are skipped (they are
    /// re-created on demand), as are files from the last hour (an upload may still be being recorded).
    /// </summary>
    public List<string> OrphanFiles() => OrphanFiles(TimeSpan.FromHours(1));

    public List<string> OrphanFiles(TimeSpan minAge)
    {
        if (!Directory.Exists(io.DocumentsPath)) return new();
        var known = db.Strings("""
            SELECT StoredPath FROM Attachments UNION ALL SELECT StoredPath FROM CircularFiles
            UNION ALL SELECT StoredPath FROM LeadAttachments UNION ALL SELECT StoredPath FROM ENoteAttachments
            UNION ALL SELECT PdfPath FROM ENotes WHERE PdfPath IS NOT NULL
            """).Select(p => p.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var recent = DateTime.UtcNow - minAge;
        var orphans = Directory.EnumerateFiles(io.DocumentsPath, "*", SearchOption.AllDirectories)
            .Where(f => File.GetLastWriteTimeUtc(f) <= recent)
            .Select(f => Path.GetRelativePath(io.DocumentsPath, f).Replace('\\', '/'))
            .Where(r => !known.Contains(r) && !r.StartsWith("enotes/pdf/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (orphans.Count == 0) return new();
        log.LogWarning("Stored files without a database record: {Files}", string.Join(", ", orphans.Take(50)));
        return new List<string> { $"Orphan files (not deleted; see the log): {orphans.Count}" };
    }

    private int DeleteInBatches(string where, string cut)
    {
        int total = 0, n;
        do
        {
            n = db.Exec($"DELETE FROM Notifications WHERE Id IN (SELECT Id FROM Notifications WHERE {where} LIMIT {Batch})", new { cut });
            total += n;
        } while (n == Batch);
        return total;
    }
}
