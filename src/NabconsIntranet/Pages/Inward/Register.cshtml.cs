using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Pages.Inward;

/// <summary>The register (DD-08 §6.2): filters, text search, 50 a page, Excel export (20,000 rows), register PDF (31 days / 5,000 rows).</summary>
public class RegisterModel(InwardRepository repo, InwardReports reports, AuditService audit) : InwardPage
{
    public InwardFilter Filter { get; private set; } = new();
    public Paged<InwardItem> Result { get; private set; } = new();
    public List<OfficeOption> Offices { get; private set; } = new();
    public List<PersonOption> Holders { get; private set; } = new();

    private void Read(InwardFilter f)
    {
        f.Q = Clean(f.Q, 100);
        f.Status = f.Status != null && InwardFilter.StatusLabels.ContainsKey(f.Status) ? f.Status : "open";
        f.From = ParseDate(f.From) is { } a ? IsoDate(a) : null;
        f.To = ParseDate(f.To) is { } b ? IsoDate(b) : null;
        f.Page = Math.Max(1, f.Page);
        Filter = f;
    }

    public IActionResult OnGet([FromQuery] InwardFilter f)
    {
        if (!Access.SeesRegister(User)) return NotFound();
        Read(f);
        Result = repo.Register(User, Filter);
        Offices = repo.Offices(false);
        Holders = repo.AllUsers();
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync([FromQuery] InwardFilter f, string? format)
    {
        if (!Access.SeesRegister(User)) return NotFound();
        Read(f);
        var pdf = format == ReportFormat.Pdf;
        if (pdf)
        {
            if (Filter.From == null || Filter.To == null) { FlashError("Choose the received from and to dates (at most 31 days) to print the register."); return Redirect(Url()); }
            var days = ParseDate(Filter.To)!.Value.DayNumber - ParseDate(Filter.From)!.Value.DayNumber + 1;
            if (days < 1 || days > InwardReports.MaxPdfDays) { FlashError($"The printed register covers at most {InwardReports.MaxPdfDays} days."); return Redirect(Url()); }
        }
        var rows = repo.RegisterRows(User, Filter, pdf ? InwardReports.MaxPdfRows : InwardRepository.MaxExport, oldestFirst: pdf);
        if (rows == null) { FlashError($"Too many rows match ({(pdf ? InwardReports.MaxPdfRows : InwardRepository.MaxExport):N0} at most). Narrow the filter."); return Redirect(Url()); }
        var office = Filter.OfficeId is long o ? repo.Office(o)?.Name : null;
        var range = Filter.From != null || Filter.To != null ? $"Received {(Filter.From != null ? "from " + F.DateDot(Filter.From) : "")} {(Filter.To != null ? "to " + F.DateDot(Filter.To) : "")}".Trim() : "All dates";
        var doc = reports.Register(rows, "Inward register" + (office != null ? " — " + office : ""), range + " · " + InwardFilter.StatusLabels[Filter.Status!], pdf);
        audit.Log("Inward.Exported", "InwardItem", null, $"{rows.Count} rows; {(pdf ? "PDF" : "Excel")}; status={Filter.Status}");
        return await Download(doc, pdf ? ReportFormat.Pdf : ReportFormat.Xlsx);
    }

    private Fmt F => HttpContext.RequestServices.GetRequiredService<Fmt>();

    public string Url(int? page = null, string? export = null)
    {
        var f = Filter;
        var q = new List<string>();
        void Add(string k, object? v) { if (v != null && v.ToString() != "") q.Add(k + "=" + Uri.EscapeDataString(v.ToString()!)); }
        if (export != null) { q.Add("handler=Export"); q.Add("format=" + export); }
        Add("OfficeId", f.OfficeId); Add("From", f.From); Add("To", f.To); Add("Type", f.Type); Add("Mode", f.Mode); Add("Status", f.Status);
        Add("HolderId", f.HolderId); Add("Priority", f.Priority); Add("Q", f.Q);
        if ((page ?? 1) > 1) q.Add("Page=" + page);
        return "/Inward/Register" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }
}
