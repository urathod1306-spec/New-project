using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>The Compliance Cell's queue: updates to review, due-date requests, closure proposals, stale overdue points.</summary>
public class ReviewModel(ComplianceRepository repo) : CompliancePage
{
    public List<ComplianceQueueRow> Updates { get; private set; } = new();
    public List<ComplianceQueueRow> Revisions { get; private set; } = new();
    public List<ComplianceQueueRow> Proposed { get; private set; } = new();
    public List<ComplianceQueueRow> Stale { get; private set; } = new();

    public IActionResult OnGet()
    {
        if (!Access.IsAdmin(User)) return NotFound();
        (Updates, Revisions, Proposed, Stale) = repo.Queue(User);
        return Page();
    }
}
