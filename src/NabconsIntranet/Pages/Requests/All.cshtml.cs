using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Requests;

public class AllModel(RequestWorkflow workflow) : AppPage
{
    public string? State { get; private set; }
    public List<ApprovalRequest> Items { get; private set; } = new();
    public Dictionary<string, long> Counts { get; private set; } = new();

    public IActionResult OnGet(string? state)
    {
        if (!User.IsAdmin() && !User.IsAuditor()) return Forbid();
        State = RequestStates.All.Contains(state) ? state : null;
        Items = workflow.All(State);
        Counts = workflow.CountsByState();
        return Page();
    }
}
