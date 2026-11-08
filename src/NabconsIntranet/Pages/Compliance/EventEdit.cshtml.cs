using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>Register or edit an event (Compliance Cell only).</summary>
public class EventEditModel(ComplianceRepository repo, ComplianceWorkflow workflow, MasterRepository masters) : CompliancePage
{
    [BindProperty] public ComplianceEventInput Input { get; set; } = new();
    [BindProperty] public long RowVersion { get; set; }
    public long Id { get; private set; }
    public bool HasPoints { get; private set; }
    public List<MasterItem> Forums { get; private set; } = new();

    private IActionResult? Load(long? id)
    {
        if (!Access.IsAdmin(User)) return NotFound();
        Id = id ?? 0;
        var forums = masters.List(MasterTypes.ComplianceForum, false);
        if (id > 0)
        {
            var e = repo.Event(id.Value);
            if (e == null || !Access.CanSeeEventManaged(e, User)) return NotFound();
            HasPoints = repo.EventPoints(e.Id, User).Count > 0;
            Forums = forums.Where(f => f.IsActive || f.Id == e.ForumId).ToList();
            if (Request.Method == "GET")
            {
                RowVersion = e.RowVersion;
                Input = new ComplianceEventInput
                {
                    SourceType = e.SourceType, ForumId = e.ForumId, EventNo = e.EventNo, Title = e.Title, HeldOn = e.HeldOn, PeriodFrom = e.PeriodFrom, PeriodTo = e.PeriodTo,
                    ReportReceivedOn = e.ReportReceivedOn, ReportedTo = e.ReportedTo, ReportingCycle = e.ReportingCycle, NextReportingOn = e.NextReportingOn,
                    IsConfidential = e.IsConfidential, SourceKeptAt = e.SourceKeptAt, Remarks = e.Remarks,
                };
            }
        }
        else Forums = forums.Where(f => f.IsActive).ToList();
        return null;
    }

    public IActionResult OnGet(long? id) => Load(id) ?? Page();

    public IActionResult OnPost(long? id)
    {
        if (Load(id) is { } r) return r;
        try
        {
            if (Id > 0) { workflow.UpdateEvent(Id, Input, RowVersion, User); Flash("Event saved."); return Redirect($"/Compliance/Event/{Id}"); }
            var newId = workflow.CreateEvent(Input, User);
            Flash("Event registered. Now add its action points, one by one or from a CSV file.");
            return Redirect($"/Compliance/Event/{newId}");
        }
        catch (WorkflowException ex) { ModelState.AddModelError("", ex.Message); return Page(); }
    }
}
