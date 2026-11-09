using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>Add or edit an action point (Compliance Cell; not on points where the member is an officer).</summary>
public class PointEditModel(ComplianceRepository repo, ComplianceWorkflow workflow, MasterRepository masters) : CompliancePage
{
    [BindProperty] public CompliancePointInput Input { get; set; } = new();
    [BindProperty] public long RowVersion { get; set; }
    public long Id { get; private set; }
    public ComplianceEvent Event { get; private set; } = new();
    public List<PersonOption> Users { get; private set; } = new();
    public List<string> Categories { get; private set; } = new();
    public List<string> Verticals { get; private set; } = new();

    private IActionResult? Load(long? id, long? eventId)
    {
        if (!Access.IsAdmin(User)) return NotFound();
        Id = id ?? 0;
        CompliancePoint? p = null;
        if (Id > 0)
        {
            p = repo.Point(Id);
            if (p == null || !Access.CanViewPoint(p, User)) return NotFound();
            eventId = p.EventId;
        }
        var e = eventId > 0 ? repo.Event(eventId.Value) : null;
        if (e == null || !Access.CanSeeEventManaged(e, User)) return NotFound();
        Event = e;
        Users = repo.ActiveUsers();
        Categories = masters.Names(MasterTypes.ComplianceCategory);
        Verticals = masters.Names(MasterTypes.Vertical);
        if (p != null)
        {
            if (p.Category != null && !Categories.Contains(p.Category)) Categories.Insert(0, p.Category);
            if (p.OwnerVertical != null && !Verticals.Contains(p.OwnerVertical)) Verticals.Insert(0, p.OwnerVertical);
            if (Users.All(x => x.Id != p.PrimaryOfficerUserId)) Users.Insert(0, new PersonOption { Id = p.PrimaryOfficerUserId, DisplayName = p.OfficerName + " (inactive)" });
            if (Request.Method == "GET")
            {
                RowVersion = p.RowVersion;
                Input = new CompliancePointInput
                {
                    RefNo = p.RefNo, RefSubject = p.RefSubject, PointText = p.PointText, Category = p.Category, Priority = p.Priority, OwnerVertical = p.OwnerVertical,
                    PrimaryOfficerUserId = p.PrimaryOfficerUserId, SupportOfficerIds = repo.SupportOfficers(p.Id).Select(x => x.Id).ToList(), OfficerCode = p.OfficerCode,
                    Amount = p.Amount, CarriedFromNumber = p.CarriedFromId is long cf ? repo.Point(cf)?.Number : null, PapersKeptAt = p.PapersKeptAt,
                };
            }
        }
        return null;
    }

    public IActionResult OnGet(long? id, long? eventId) => Load(id, eventId) ?? Page();

    public IActionResult OnPost(long? id, long? eventId)
    {
        if (Load(id, eventId) is { } r) return r;
        try
        {
            if (Id > 0) { workflow.UpdatePoint(Id, Input, RowVersion, User); Flash("Action point saved."); return Redirect($"/Compliance/Point/{Id}"); }
            var newId = workflow.CreatePoint(Event.Id, Input, User);
            Flash("Action point added and the officer notified.");
            return Redirect(Request.Form["another"] == "1" ? $"/Compliance/PointEdit?eventId={Event.Id}" : $"/Compliance/Point/{newId}");
        }
        catch (WorkflowException ex) { ModelState.AddModelError("", ex.Message); return Page(); }
    }
}
