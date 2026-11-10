using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Inward;

/// <summary>Project codes (DD-08 §6.8): list and CSV import (upload → preview → confirm), in memory only.</summary>
public class ProjectCodesModel(InwardRepository repo, InwardWorkflow workflow, InwardOptions o) : InwardPage
{
    public List<ProjectCode> Codes { get; private set; } = new();
    public List<ProjectCodeRow> Rows { get; private set; } = new();
    public string? Text { get; private set; }
    public InwardOptions Options => o;

    public IActionResult OnGet()
    {
        if (!Access.IsAccounts(User)) return NotFound();
        Codes = repo.ProjectCodes(false);
        return Page();
    }

    public IActionResult OnGetTemplate() => Access.IsAccounts(User) ? File(InwardWorkflow.ProjectTemplate(), "text/csv", "project-codes-template.csv") : NotFound();

    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file)
    {
        if (!Access.IsAccounts(User)) return NotFound();
        var text = await ReadCsv(file, o.MaxImportKB);
        if (text == null) return RedirectToPage();
        Rows = workflow.ParseProjectCodes(text, out var error);
        if (error != null) { FlashError(error); return RedirectToPage(); }
        Text = ComplianceImport.Normalise(text);
        Codes = repo.ProjectCodes(false);
        return Page();
    }

    public IActionResult OnPostConfirm(string? csv, bool deactivate)
    {
        if (!Access.IsAccounts(User)) return NotFound();
        if (string.IsNullOrEmpty(csv)) { FlashError("The preview has expired. Upload the file again."); return RedirectToPage(); }
        try { var n = workflow.ImportProjectCodes(csv, deactivate, User); Flash($"{n} project code(s) added, changed or deactivated."); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage();
    }

    /// <summary>Reads an uploaded CSV into memory (never to disk); null with a flashed reason when it is unusable.</summary>
    internal async Task<string?> ReadCsv(IFormFile? file, int maxKb)
    {
        if (file == null || file.Length == 0) { FlashError("Choose a CSV file."); return null; }
        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)) { FlashError("The file must be a .csv file."); return null; }
        if (file.Length > maxKb * 1024L) { FlashError($"CSV files are limited to {maxKb:N0} KB."); return null; }
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var text = ComplianceImport.Decode(ms.ToArray(), out var err);
        if (text == null) FlashError(err!);
        return text;
    }
}
