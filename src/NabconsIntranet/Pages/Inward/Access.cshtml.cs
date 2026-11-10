using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Inward;

/// <summary>Inward access grants (DD-08 §6.9, E3): Administrator only, never to themselves; history kept.</summary>
public class AccessModel(InwardRepository repo, InwardWorkflow workflow) : InwardPage
{
    public List<InwardMember> Members { get; private set; } = new();
    public List<InwardMemberChange> History { get; private set; } = new();
    public List<OfficeOption> Offices { get; private set; } = new();
    public List<PersonOption> Users { get; private set; } = new();
    public List<string> NoOperator { get; private set; } = new();

    public IActionResult OnGet()
    {
        if (!User.IsAdmin()) return NotFound();
        Members = repo.Members();
        History = repo.MemberHistory();
        Offices = repo.Offices();
        Users = repo.ActiveUsers().Where(u => u.Id != User.UserId()).ToList();
        NoOperator = repo.OfficesWithoutOperator();
        return Page();
    }

    public IActionResult OnPostGrant(long userId, string? role, long officeId)
    {
        if (!User.IsAdmin()) return NotFound();
        try { workflow.Grant(userId, role ?? "", officeId, User); Flash("Access granted and the user notified."); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage();
    }

    public IActionResult OnPostRevoke(long memberId)
    {
        if (!User.IsAdmin()) return NotFound();
        try { workflow.Revoke(memberId, User); Flash("Access removed."); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage();
    }
}
