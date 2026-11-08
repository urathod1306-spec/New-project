using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>Compliance access grants, managed only by Compliance Admins (DD-07 D12).</summary>
public class MembersModel(ComplianceRepository repo, ComplianceWorkflow workflow) : CompliancePage
{
    public List<ComplianceMember> Members { get; private set; } = new();
    public List<ComplianceMemberChange> Changes { get; private set; } = new();
    public List<PersonOption> Users { get; private set; } = new();

    public IActionResult OnGet()
    {
        if (!Access.IsAdmin(User)) return NotFound();
        Members = repo.Members();
        Changes = repo.MemberHistory();
        Users = repo.ActiveUsers();
        return Page();
    }

    public IActionResult OnPostGrant(long userId, string? role)
    {
        if (!Access.IsAdmin(User)) return NotFound();
        try { workflow.Grant(userId, role ?? "", User); Flash("Access granted. The Compliance Cell has been notified."); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage();
    }

    public IActionResult OnPostRevoke(long userId)
    {
        if (!Access.IsAdmin(User)) return NotFound();
        try { workflow.Revoke(userId, User); Flash("Access revoked. The Compliance Cell has been notified."); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage();
    }
}
