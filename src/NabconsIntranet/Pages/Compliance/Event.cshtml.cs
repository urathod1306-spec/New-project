using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>Event header and its action points (officers see only their own points).</summary>
public class EventModel(ComplianceRepository repo) : CompliancePage
{
    public ComplianceEvent Item { get; private set; } = new();
    public List<CompliancePoint> Points { get; private set; } = new();
    public bool IsAdmin { get; private set; }
    public bool Managed { get; private set; }

    public IActionResult OnGet(long id)
    {
        var e = repo.Event(id);
        if (e == null || !repo.CanViewEvent(id, User)) return NotFound();
        Item = e;
        Points = repo.EventPoints(id, User);
        Managed = Access.CanSeeEventManaged(e, User);
        IsAdmin = Access.IsAdmin(User) && Managed;
        return Page();
    }
}
