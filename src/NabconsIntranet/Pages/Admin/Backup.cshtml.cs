using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

public class BackupModel(BackupService backups, AuditService audit, IntranetOptions o) : AppPage
{
    public List<BackupInfo> Items { get; private set; } = new();
    public IntranetOptions Options => o;

    public void OnGet() => Items = backups.List();

    public IActionResult OnPostCreate()
    {
        try
        {
            var b = backups.Create(audit, User.UserName());
            Flash($"Backup created: {b.FileName} ({Fmt.Size(b.SizeBytes)}).");
        }
        catch (Exception ex)
        {
            audit.Log("Backup.Failed", "Backup", null, ex.Message);
            FlashError("Backup failed: " + ex.Message);
        }
        return RedirectToPage();
    }

    public IActionResult OnPostVerify(string name)
    {
        var b = backups.List().FirstOrDefault(x => x.FileName == name);
        if (b == null) return NotFound();
        var actual = BackupService.HashFile(b.FullPath);
        var ok = string.Equals(actual, b.Sha256, StringComparison.OrdinalIgnoreCase);
        audit.Log(ok ? "Backup.Verified" : "Backup.VerifyFailed", "Backup", b.FileName, actual);
        if (ok) Flash($"{b.FileName}: checksum OK."); else FlashError($"{b.FileName}: CHECKSUM MISMATCH — this backup is damaged or altered.");
        return RedirectToPage();
    }
}
