using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>
/// Bulk import of action points from CSV (DD-07 §6): upload → preview → confirm. The file is read into memory and
/// never written to disk; the preview carries the text in a hidden field, and confirm (posted as multipart, the only
/// page where UploadGuard allows it) re-checks the limits and re-validates every row before inserting all or none.
/// </summary>
public class ImportModel(ComplianceRepository repo, ComplianceImport import, ComplianceWorkflow workflow, ComplianceOptions o) : CompliancePage
{
    public ComplianceEvent Event { get; private set; } = new();
    public List<ComplianceImport.Row> Rows { get; private set; } = new();
    public string? Text { get; private set; }
    public string? FileName { get; private set; }
    public ComplianceOptions Options => o;

    private IActionResult? Load(long eventId)
    {
        if (!Access.IsAdmin(User)) return NotFound();
        var e = repo.Event(eventId);
        if (e == null || !Access.CanSeeEventManaged(e, User)) return NotFound();
        Event = e;
        return null;
    }

    public IActionResult OnGet(long eventId) => Load(eventId) ?? Page();

    public IActionResult OnGetTemplate(long eventId) =>
        Load(eventId) ?? File(ComplianceImport.Template(), "text/csv", "compliance-import-template.csv");

    public async Task<IActionResult> OnPostPreviewAsync(long eventId, IFormFile? file)
    {
        if (Load(eventId) is { } r) return r;
        if (file == null || file.Length == 0) { FlashError("Choose a CSV file."); return RedirectToPage(new { eventId }); }
        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)) { FlashError("The file must be a .csv file."); return RedirectToPage(new { eventId }); }
        if (file.Length > o.MaxImportKB * 1024L) { FlashError($"CSV files are limited to {o.MaxImportKB:N0} KB."); return RedirectToPage(new { eventId }); }
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);                     // in memory only
        var text = ComplianceImport.Decode(ms.ToArray(), out var decodeError);
        if (text == null) { FlashError(decodeError!); return RedirectToPage(new { eventId }); }
        Rows = import.Parse(text, Access.IsAdmin(User) ? User.UserId() : 0, out var headerError);
        if (headerError != null) { FlashError(headerError); return RedirectToPage(new { eventId }); }
        Text = ComplianceImport.Normalise(text);
        FileName = Path.GetFileName(file.FileName);
        return Page();
    }

    public IActionResult OnPostConfirm(long eventId, string? csv)
    {
        if (Load(eventId) is { } r) return r;
        if (string.IsNullOrEmpty(csv)) { FlashError("The preview has expired. Upload the file again."); return RedirectToPage(new { eventId }); }
        var text = ComplianceImport.Normalise(csv);
        var rows = import.Parse(text, User.UserId(), out var headerError);
        if (headerError != null) { FlashError(headerError); return RedirectToPage(new { eventId }); }
        if (rows.Any(x => x.Errors.Count > 0)) { FlashError("The file still has errors. Correct them and upload it again."); return RedirectToPage(new { eventId }); }
        try
        {
            var numbers = workflow.ImportPoints(eventId, rows.Select(x => x.Input).ToList(), ComplianceImport.Sha(text), User);
            Flash($"{numbers.Count} action point(s) imported ({numbers.First()} to {numbers.Last()}). The officers have been notified.");
            return Redirect($"/Compliance/Event/{eventId}");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); return RedirectToPage(new { eventId }); }
    }
}
