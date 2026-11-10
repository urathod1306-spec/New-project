using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Pages.Inward;

/// <summary>Invoices (DD-08 §6.7): dashboard tiles and breakdowns, the filtered list, Excel exports and the MSME / 43B(h) report.</summary>
public class InvoicesModel(InwardRepository repo, InwardReports reports, InwardOptions o, AuditService audit, Fmt fmt) : InwardPage
{
    public InvoiceDashboard D { get; private set; } = new();
    public InvoiceFilter Filter { get; private set; } = new();
    public Paged<InwardItem> Result { get; private set; } = new();
    public List<ProjectCode> Projects { get; private set; } = new();
    public InwardOptions Options => o;
    public string DefaultAsOn { get; private set; } = "";

    private bool Allowed => Access.IsViewer(User) || Access.IsAccounts(User) || Access.IsAuditor(User);

    private void Read(InvoiceFilter f)
    {
        f.Status = f.Status != null && InvoiceFilter.StatusLabels.ContainsKey(f.Status) ? f.Status : "open";
        f.Q = Clean(f.Q, 100); f.Project = Clean(f.Project, 50); f.Page = Math.Max(1, f.Page);
        Filter = f;
    }

    public IActionResult OnGet([FromQuery] InvoiceFilter f)
    {
        if (!Allowed) return NotFound();
        Read(f);
        D = repo.InvoiceDashboard(User);
        Result = repo.Invoices(User, Filter);
        Projects = repo.ProjectCodes(false);
        var t = fmt.Today;
        DefaultAsOn = new DateOnly(t.Month >= 4 ? t.Year : t.Year - 1, 3, 31).ToString("yyyy-MM-dd");
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync([FromQuery] InvoiceFilter f)
    {
        if (!Allowed) return NotFound();
        Read(f);
        var rows = repo.InvoiceRows(User, Filter);
        if (rows == null) { FlashError($"More than {InwardRepository.MaxExport:N0} invoices match. Narrow the filter."); return Redirect("/Inward/Invoices"); }
        audit.Log("Inward.InvoicesExported", "InvoiceDetails", null, $"{rows.Count} rows; status={Filter.Status}");
        return await Download(reports.InvoiceList(rows, InvoiceFilter.StatusLabels[Filter.Status!]), ReportFormat.Xlsx);
    }

    public async Task<IActionResult> OnGetMsmeAsync(string? asOn, string? enteredBy)
    {
        if (!Allowed) return NotFound();
        var a = ParseDate(asOn);
        if (a == null) { FlashError("Choose the as-on date for the MSME report."); return Redirect("/Inward/Invoices"); }
        var e = ParseDate(enteredBy);
        var rows = repo.MsmeReport(User, a.Value, e);
        audit.Log("Inward.MsmeReport", "InvoiceDetails", null, $"as on {a:yyyy-MM-dd}; {rows.Count} rows");
        return await Download(reports.Msme(rows, a.Value, e), ReportFormat.Xlsx);
    }

    public string Url(int? page = null, bool export = false)
    {
        var q = new List<string>();
        void Add(string k, object? v) { if (v != null && v.ToString() != "") q.Add(k + "=" + Uri.EscapeDataString(v.ToString()!)); }
        if (export) q.Add("handler=Export");
        Add("Status", Filter.Status); Add("Project", Filter.Project); Add("Q", Filter.Q);
        if ((page ?? 1) > 1) q.Add("Page=" + page);
        return "/Inward/Invoices" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }
}
