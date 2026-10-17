using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Leads;

public class DashboardModel(LeadRepository leads, LeadAccess access, Fmt fmt) : AppPage
{
    public sealed record Row(string Label, int Count, double Value);

    public string Fy { get; private set; } = "";
    public List<string> Years { get; private set; } = new();
    public List<Row> ByStatus { get; private set; } = new();
    public List<Row> BySource { get; private set; } = new();
    public List<Row> ByVertical { get; private set; } = new();
    public List<Lead> Upcoming { get; private set; } = new();
    public int Total, Won, Lost;
    public double WonValue, PipelineValue;
    public double? Conversion => Won + Lost == 0 ? null : 100.0 * Won / (Won + Lost);

    public IActionResult OnGet(string? fy)
    {
        if (!access.CanManage(User)) return StatusCode(403);
        Years = leads.FinancialYears();
        var current = LeadRepository.FinancialYear(fmt.Today);
        if (!Years.Contains(current)) Years.Insert(0, current);
        Fy = fy == "All" ? "All" : Years.Contains(fy ?? "") ? fy! : current;
        var list = leads.All(Fy == "All" ? null : Fy);
        Total = list.Count;
        Won = list.Count(l => l.Status == LeadStatus.Won);
        Lost = list.Count(l => l.Status == LeadStatus.Lost);
        WonValue = list.Where(l => l.Status == LeadStatus.Won).Sum(l => l.AwardedValueLakh ?? 0);
        PipelineValue = list.Where(l => !LeadStatus.IsClosed(l.Status)).Sum(l => l.EstimatedValueLakh ?? 0);
        ByStatus = LeadStatus.All.Select(s => new Row(s, list.Count(l => l.Status == s), list.Where(l => l.Status == s).Sum(l => l.EstimatedValueLakh ?? 0))).ToList();
        BySource = list.GroupBy(l => l.Source ?? "Not stated").Select(g => new Row(g.Key, g.Count(), g.Sum(l => l.EstimatedValueLakh ?? 0))).OrderByDescending(r => r.Count).ToList();
        ByVertical = list.GroupBy(l => l.SubmitterVertical ?? "Not stated").Select(g => new Row(g.Key, g.Count(), g.Sum(l => l.EstimatedValueLakh ?? 0))).OrderByDescending(r => r.Count).ToList();
        var today = fmt.Today.ToString("yyyy-MM-dd");
        Upcoming = leads.All(null).Where(l => !LeadStatus.IsClosed(l.Status) && !string.IsNullOrEmpty(l.DueDate) && string.CompareOrdinal(l.DueDate, today) >= 0)
            .OrderBy(l => l.DueDate).Take(10).ToList();
        return Page();
    }
}
