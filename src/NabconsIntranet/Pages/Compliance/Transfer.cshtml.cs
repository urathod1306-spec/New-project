using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>Bulk transfer of open action points from one officer to another (e.g. on transfer or retirement).</summary>
public class TransferModel(ComplianceRepository repo, ComplianceWorkflow workflow) : CompliancePage
{
    public List<PersonOption> Users { get; private set; } = new();
    public List<PersonOption> AllUsers { get; private set; } = new();
    public ComplianceWorkflow.TransferPlan? Plan { get; private set; }
    [BindProperty(SupportsGet = true)] public long From { get; set; }
    [BindProperty(SupportsGet = true)] public long To { get; set; }
    [BindProperty(SupportsGet = true)] public string Scope { get; set; } = "both";

    private IActionResult? Load()
    {
        if (!Access.IsAdmin(User)) return NotFound();
        Users = repo.ActiveUsers();
        AllUsers = repo.AllUsers();
        return null;
    }

    public IActionResult OnGet()
    {
        if (Load() is { } r) return r;
        if (From > 0) Plan = workflow.PlanTransfer(From, Scope, User);
        return Page();
    }

    public IActionResult OnPost()
    {
        if (Load() is { } r) return r;
        try { var n = workflow.Transfer(From, To, Scope, User); Flash($"{n} action point(s) transferred. Both officers have been notified."); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage();
    }
}
