using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Inward;

/// <summary>ERP payment import (DD-08 §6.11; R2-12): upload → preview by category → confirm the "matched — new" rows.</summary>
public class PaymentsModel(InwardWorkflow workflow, InwardOptions o) : InwardPage
{
    public List<PaymentImportRow> Rows { get; private set; } = new();
    public string? Text { get; private set; }
    public InwardOptions Options => o;

    public IActionResult OnGet() => Access.IsAccounts(User) ? Page() : NotFound();

    public IActionResult OnGetTemplate() => Access.IsAccounts(User) ? File(InwardWorkflow.PaymentTemplate(), "text/csv", "erp-payments-template.csv") : NotFound();

    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file)
    {
        if (!Access.IsAccounts(User)) return NotFound();
        if (file == null || file.Length == 0) { FlashError("Choose a CSV file."); return RedirectToPage(); }
        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)) { FlashError("The file must be a .csv file."); return RedirectToPage(); }
        if (file.Length > o.MaxImportKB * 1024L) { FlashError($"CSV files are limited to {o.MaxImportKB:N0} KB."); return RedirectToPage(); }
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var text = ComplianceImport.Decode(ms.ToArray(), out var err);
        if (text == null) { FlashError(err!); return RedirectToPage(); }
        try
        {
            Rows = workflow.PreviewPayments(text, User, out var error);
            if (error != null) { FlashError(error); return RedirectToPage(); }
        }
        catch (WorkflowException ex) { FlashError(ex.Message); return RedirectToPage(); }
        Text = ComplianceImport.Normalise(text);
        return Page();
    }

    public IActionResult OnPostConfirm(string? csv)
    {
        if (!Access.IsAccounts(User)) return NotFound();
        if (string.IsNullOrEmpty(csv)) { FlashError("The preview has expired. Upload the file again."); return RedirectToPage(); }
        try { var n = workflow.ImportPayments(csv, User); Flash($"{n} payment(s) recorded."); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage();
    }
}
