using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Leads;

/// <summary>Lead pipeline — BDU vertical and LeadViewer (COO/MD) only.</summary>
public class IndexModel(LeadRepository leads, LeadAccess access, MasterRepository masters, AuditService audit, Fmt fmt) : AppPage
{
    public LeadFilter Filter { get; private set; } = new();
    public List<Lead> Items { get; private set; } = new();
    public List<string> ClientTypes { get; private set; } = new();
    public List<string> Sectors { get; private set; } = new();
    public List<string> Years { get; private set; } = new();
    public List<AppUser> Owners { get; private set; } = new();
    public string Today => fmt.Today.ToString("yyyy-MM-dd");
    public string Soon => fmt.Today.AddDays(7).ToString("yyyy-MM-dd");

    private LeadFilter Read(string? q, string? status, string? clientType, string? sector, long? owner, string? submitter, string? fy, bool dueSoon, string? sort) => new()
    {
        Q = Clean(q, 100), Status = Clean(status, 40), ClientType = Clean(clientType, 100), Sector = Clean(sector, 100), OwnerUserId = owner,
        Submitter = Clean(submitter, 100), FinancialYear = Clean(fy, 10), DueSoon = dueSoon, Sort = Clean(sort, 10) ?? "updated",
    };

    public IActionResult OnGet(string? q, string? status, string? clientType, string? sector, long? owner, string? submitter, string? fy, bool dueSoon = false, string? sort = null)
    {
        if (!access.CanManage(User)) return StatusCode(403);
        Filter = Read(q, status, clientType, sector, owner, submitter, fy, dueSoon, sort);
        Items = leads.Pipeline(Filter);
        ClientTypes = masters.Names(MasterTypes.ClientType);
        Sectors = masters.Names(MasterTypes.LeadSector);
        Years = leads.FinancialYears();
        Owners = leads.BduMembers();
        return Page();
    }

    public IActionResult OnGetExport(string? q, string? status, string? clientType, string? sector, long? owner, string? submitter, string? fy, bool dueSoon = false, string? sort = null)
    {
        if (!access.CanManage(User)) return StatusCode(403);
        var f = Read(q, status, clientType, sector, owner, submitter, fy, dueSoon, sort);
        var rows = leads.Pipeline(f).Select(l => (IReadOnlyList<object?>)new object?[]
        {
            l.Reference, l.FinancialYear, l.Title, l.ClientName, l.ClientType, l.Sector, l.StateLocation, l.Source, l.EstimatedValueLakh,
            l.DueDate, l.Status, l.OwnerName, l.SubmitterName, l.SubmitterVertical, l.TenderNumber, l.AwardedValueLakh, l.AwardDate,
            l.LossReason, l.LossReasonText, fmt.DateTime(l.CreatedAt), fmt.DateTime(l.UpdatedAt),
        });
        var bytes = XlsxWriter.Build("Lead pipeline", new[]
        {
            "Reference", "FY", "Title", "Client", "Client type", "Sector", "State / location", "Source", "Est. fee (₹ lakh)", "Due date",
            "Status", "Owner", "Submitted by", "Submitter vertical", "Tender no.", "Awarded (₹ lakh)", "Award date", "Reason", "Reason details",
            "Submitted on", "Last updated",
        }, rows);
        audit.Log("Lead.Exported", "Leads", null, $"Pipeline exported (status={f.Status}; fy={f.FinancialYear}; q={f.Q})");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"lead-pipeline-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }

    public string Url(string? sort = null, bool export = false)
    {
        var f = Filter;
        return $"/Leads?{(export ? "handler=Export&" : "")}q={Uri.EscapeDataString(f.Q ?? "")}&status={Uri.EscapeDataString(f.Status ?? "")}" +
               $"&clientType={Uri.EscapeDataString(f.ClientType ?? "")}&sector={Uri.EscapeDataString(f.Sector ?? "")}&owner={f.OwnerUserId}" +
               $"&submitter={Uri.EscapeDataString(f.Submitter ?? "")}&fy={Uri.EscapeDataString(f.FinancialYear ?? "")}&dueSoon={f.DueSoon.ToString().ToLowerInvariant()}" +
               $"&sort={Uri.EscapeDataString(sort ?? f.Sort)}";
    }
}
