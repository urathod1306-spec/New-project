using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Inward;

/// <summary>Bulk transfer of a user's open items (DD-08 §6.12; R2-10): preview counts, then one transaction.</summary>
public class TransferModel(InwardRepository repo, InwardWorkflow workflow) : InwardPage
{
    public List<PersonOption> AllUsers { get; private set; } = new();
    public List<PersonOption> Active { get; private set; } = new();
    public TransferPreview? Preview { get; private set; }
    public long From { get; private set; }
    public long To { get; private set; }

    private bool Allowed => Access.IsViewer(User) || User.IsAdmin();

    public IActionResult OnGet(long? from, long? to)
    {
        if (!Allowed) return NotFound();
        AllUsers = repo.AllUsers();
        Active = repo.ActiveUsers();
        if (from > 0 && to > 0 && from != to) { From = from.Value; To = to.Value; Preview = workflow.PreviewTransfer(From, To); }
        return Page();
    }

    public IActionResult OnPost(long from, long to, string? remarks)
    {
        if (!Allowed) return NotFound();
        try { var n = workflow.Transfer(from, to, remarks, User); Flash($"{n} item(s) transferred and the new holder notified."); return RedirectToPage(); }
        catch (WorkflowException ex) { FlashError(ex.Message); return RedirectToPage(new { from, to }); }
    }
}
