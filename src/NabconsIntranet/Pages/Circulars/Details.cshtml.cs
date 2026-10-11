using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Circulars;

public class DetailsModel(CircularRepository circulars, FileStore files, AuditService audit) : AppPage
{
    public Circular Item { get; private set; } = new();
    public List<CircularFile> Files { get; private set; } = new();
    public List<Circular> Replaces { get; private set; } = new();
    public string Accept => files.AcceptAttribute;
    public string AllowedList => files.AllowedList;

    public IActionResult OnGet(long id)
    {
        var c = circulars.GetVisible(id, User);
        if (c == null) return NotFound();
        Item = c;
        Files = circulars.Files(id);
        Replaces = circulars.SupersededBy(id);
        return Page();
    }

    public IActionResult OnPostPublish(long id)
    {
        if (!User.IsPublisher()) return Forbid();
        var c = circulars.Get(id);
        if (c == null) return NotFound();
        if (c.Status != CircularStatus.Draft) { FlashError("Only draft circulars can be published."); return RedirectToPage(new { id }); }
        if (c.CurrentVersion == 0) { FlashError("Upload the circular file before publishing."); return RedirectToPage(new { id }); }
        var superseded = circulars.Publish(id, User.UserName());
        audit.Log("Circular.Published", "Circular", id, c.CircularNumber);
        if (superseded != null)
            audit.Log("Circular.Superseded", "Circular", superseded, $"Superseded by {c.CircularNumber}");
        Flash($"{c.CircularNumber} has been published.");
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostWithdraw(long id, string? reason)
    {
        if (!User.IsPublisher()) return Forbid();
        var c = circulars.Get(id);
        if (c == null) return NotFound();
        reason = Clean(reason, 500);
        if (reason == null) { FlashError("A reason is required to withdraw a circular."); return RedirectToPage(new { id }); }
        if (c.Status != CircularStatus.Published) { FlashError("Only published circulars can be withdrawn."); return RedirectToPage(new { id }); }
        circulars.Withdraw(id, reason, User.UserName());
        audit.Log("Circular.Withdrawn", "Circular", id, $"{c.CircularNumber}: {reason}");
        Flash($"{c.CircularNumber} has been withdrawn.");
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostUploadAsync(long id, IFormFile? file)
    {
        if (!User.IsPublisher()) return Forbid();
        var c = circulars.Get(id);
        if (c == null) return NotFound();
        if (c.Status == CircularStatus.Withdrawn) { FlashError("Withdrawn circulars cannot be changed."); return RedirectToPage(new { id }); }
        if (file == null) { FlashError("Choose a file to upload."); return RedirectToPage(new { id }); }
        try
        {
            var stored = await files.SaveAsync(file, "circulars");
            var version = circulars.AddFile(id, stored, User.UserName());
            audit.Log("Circular.FileUploaded", "Circular", id, $"{c.CircularNumber} v{version}: {stored.OriginalFileName} ({stored.SizeBytes} bytes, SHA-256 {stored.Sha256})");
            Flash($"Version {version} uploaded.");
        }
        catch (UploadRejectedException ex)
        {
            audit.Log("Upload.Rejected", "Circular", id, ex.Message);
            FlashError(ex.Message);
        }
        return RedirectToPage(new { id });
    }
}
