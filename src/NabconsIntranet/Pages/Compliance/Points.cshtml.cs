using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>Drill-down list of action points with filters and Excel export (DD-07 §6, §7.7).</summary>
public class PointsModel(ComplianceRepository repo, MasterRepository masters, AuditService audit, Fmt F, IntranetOptions Site) : CompliancePage
{
    public CompliancePointFilter Filter { get; private set; } = new();
    public Paged<CompliancePoint> Result { get; private set; } = new();
    public bool Lakh { get; private set; }
    public List<MasterItem> Forums { get; private set; } = new();
    public List<string> Categories { get; private set; } = new();
    public List<string> Verticals { get; private set; } = new();
    public List<PersonOption> Officers { get; private set; } = new();

    private void Read(string? source, long? forum, long? eventId, string? status, string? vertical, long? officer, string? category, string? priority, string? q, int page, bool lakh)
    {
        Filter = new CompliancePointFilter
        {
            Source = Clean(source, 40), ForumId = forum, EventId = eventId, Status = CompliancePointFilter.StatusLabels.ContainsKey(status ?? "") ? status : "open",
            Vertical = Clean(vertical, 150), OfficerId = officer, Category = Clean(category, 150), Priority = Clean(priority, 10), Q = Clean(q, 100), Page = Math.Max(1, page),
        };
        Lakh = lakh;
    }

    public IActionResult OnGet(string? source, long? forum, long? eventId, string? status, string? vertical, long? officer, string? category, string? priority, string? q, int page = 1, bool lakh = false)
    {
        if (!Access.SeesAll(User)) return NotFound();
        Read(source, forum, eventId, status, vertical, officer, category, priority, q, page, lakh);
        Result = repo.Points(User, Filter);
        Forums = masters.List(MasterTypes.ComplianceForum, false);
        Categories = masters.Names(MasterTypes.ComplianceCategory);
        Verticals = masters.Names(MasterTypes.Vertical);
        Officers = repo.ActiveUsers();
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(string? source, long? forum, long? eventId, string? status, string? vertical, long? officer, string? category, string? priority, string? q, bool lakh = false)
    {
        if (!Access.SeesAll(User)) return NotFound();
        Read(source, forum, eventId, status, vertical, officer, category, priority, q, 1, lakh);
        var rows = repo.ExportRows(User, Filter);
        if (rows == null) { FlashError($"More than {ComplianceRepository.MaxExport:N0} rows match. Narrow the filter and export again."); return RedirectToPage(new { source, forum, eventId, status, vertical, officer, category, priority, q, lakh }); }
        var today = F.Today;
        var doc = new ReportDocument
        {
            OrganisationLine = Site.OrganisationName, Title = "Compliance action points", Subtitles = { $"As on {F.DateDot(today.ToString("yyyy-MM-dd"))} · {rows.Count} point(s)" },
            Footer = "Exported from NABCONS Intranet", GeneratedAt = DateTime.UtcNow, FileBase = $"compliance-points-{today:yyyyMMdd}",
            Sections =
            {
                new ReportSection
                {
                    SheetName = "Action points",
                    Columns = new[] { "Number", "Source type", "Forum", "Event", "Ref no.", "Subject", "Action point", "Category", "Priority", "Vertical", "Primary officer", "Officer code",
                        "Raised on", "Original due", "Current due", "Status", "ATR text", "As on", Lakh ? "Amount (₹ lakh)" : "Amount (₹)", "Days overdue", "Papers kept at", "Closed on", "Closure authority" }
                        .Select(h => new ReportColumn(h, h is "Action point" or "ATR text" ? 4 : 1.2f)).ToList(),
                    Rows = rows.Select(p => new ReportRow(new[]
                    {
                        p.Number, ComplianceSource.Label(p.SourceType), p.Forum, (p.EventNo != null ? p.EventNo + " — " : "") + p.EventTitle, p.RefNo ?? "", p.RefSubject ?? "",
                        p.PointText, p.Category ?? "", p.Priority, p.OwnerVertical ?? "", p.OfficerName, p.OfficerCode ?? "",
                        F.DateDot(p.RaisedOn), F.DateDot(p.OriginalDueDate), F.DateDot(p.CurrentDueDate), StatusLabel(p.Status), p.AtrText ?? "", F.DateDot(p.AtrAsOn),
                        p.Amount.HasValue ? (Lakh ? (p.Amount.Value / 100000d).ToString("0.00") : p.Amount.Value.ToString("0.00")) : "",
                        p.IsOverdue(today) ? (today.DayNumber - DateOnly.ParseExact(p.CurrentDueDate, "yyyy-MM-dd").DayNumber).ToString() : "",
                        p.PapersKeptAt ?? "", F.DateDot(p.ClosedAt), p.ClosureAuthority ?? "",
                    })).ToList(),
                },
            },
        };
        audit.Log("Compliance.Exported", "CompliancePoint", null, $"{rows.Count} rows; filter status={Filter.Status}");
        return await Download(doc, ReportFormat.Xlsx);
    }

    public string Url(int? page = null, bool export = false)
    {
        var f = Filter;
        var q = new List<string>();
        void Add(string k, object? v) { if (v != null && v.ToString() != "") q.Add(k + "=" + Uri.EscapeDataString(v.ToString()!)); }
        if (export) q.Add("handler=Export");
        Add("source", f.Source); Add("forum", f.ForumId); Add("eventId", f.EventId); Add("status", f.Status); Add("vertical", f.Vertical); Add("officer", f.OfficerId);
        Add("category", f.Category); Add("priority", f.Priority); Add("q", f.Q); if (Lakh) q.Add("lakh=true");
        if ((page ?? 1) > 1) q.Add("page=" + page);
        return "/Compliance/Points" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }
}
