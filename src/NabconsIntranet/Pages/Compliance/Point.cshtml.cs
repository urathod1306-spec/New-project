using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>One action point: details, updates, revisions, history, and the officer and Compliance Cell actions (DD-07 §3.3).</summary>
public class PointModel(ComplianceRepository repo, ComplianceWorkflow workflow) : CompliancePage
{
    public CompliancePoint P { get; private set; } = new();
    public List<ComplianceUpdate> Updates { get; private set; } = new();
    public List<ComplianceRevision> Revisions { get; private set; } = new();
    public List<ComplianceHistoryEntry> History { get; private set; } = new();
    public List<PersonOption> Support { get; private set; } = new();
    public List<PersonOption> Users { get; private set; } = new();
    public bool IsOfficer { get; private set; }
    public bool IsPrimary { get; private set; }
    /// <summary>Compliance Admin who may act on this point (not one of its officers).</summary>
    public bool CanCell { get; private set; }
    public bool IsAdmin { get; private set; }
    public CompliancePoint? CarriedFrom { get; private set; }
    public CompliancePoint? MergedInto { get; private set; }
    public ComplianceUpdate? Pending { get; private set; }
    public ComplianceRevision? PendingRevision { get; private set; }

    private IActionResult? Load(long id)
    {
        var p = repo.Point(id);
        if (p == null || !Access.CanViewPoint(p, User)) return NotFound();
        P = p;
        var uid = User.UserId();
        IsOfficer = Access.IsOfficer(p.Id, uid);
        IsPrimary = p.PrimaryOfficerUserId == uid;
        IsAdmin = Access.IsAdmin(User);
        CanCell = IsAdmin && !IsOfficer;
        Updates = repo.Updates(id);
        Revisions = repo.Revisions(id);
        History = repo.History(id);
        Support = repo.SupportOfficers(id);
        Pending = p.PendingUpdateId is long u ? Updates.FirstOrDefault(x => x.Id == u) : null;
        PendingRevision = p.PendingRevisionId is long r ? Revisions.FirstOrDefault(x => x.Id == r) : null;
        if (p.CarriedFromId is long cf && repo.Point(cf) is { } c && Access.CanViewPoint(c, User)) CarriedFrom = c;
        if (p.MergedIntoId is long mi && repo.Point(mi) is { } m && Access.CanViewPoint(m, User)) MergedInto = m;
        if (CanCell) Users = repo.ActiveUsers();
        return null;
    }

    public IActionResult OnGet(long id) => Load(id) ?? Page();

    private IActionResult Act(long id, Action action, string done)
    {
        if (Load(id) is { } r) return r;
        try { action(); Flash(done); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return Redirect($"/Compliance/Point/{id}");
    }

    public IActionResult OnPostUpdate(long id, string? asOn, string? text, string? proposed, string? papers, long rv) =>
        Act(id, () => workflow.SubmitUpdate(id, asOn, text, proposed, papers, rv, User), "Compliance submitted to the Compliance Cell for review.");
    public IActionResult OnPostWithdrawUpdate(long id, long updateId, long rv) => Act(id, () => workflow.WithdrawUpdate(id, updateId, User, rv), "Update withdrawn.");
    public IActionResult OnPostRevision(long id, string? newDate, string? reason, long rv) => Act(id, () => workflow.RequestRevision(id, newDate, reason, rv, User), "New due date requested.");
    public IActionResult OnPostWithdrawRevision(long id, long revisionId, long rv) => Act(id, () => workflow.WithdrawRevision(id, revisionId, User, rv), "Request withdrawn.");
    public IActionResult OnPostAccept(long id, long updateId, string? atrText, long rv) => Act(id, () => workflow.AcceptUpdate(id, updateId, atrText, rv, User), "Update accepted.");
    public IActionResult OnPostReturn(long id, long updateId, string? remarks, long rv) => Act(id, () => workflow.ReturnUpdate(id, updateId, remarks, rv, User), "Update returned to the officer.");
    public IActionResult OnPostRejectProposal(long id, string? remarks, long rv) => Act(id, () => workflow.RejectClosureProposal(id, remarks, rv, User), "Closure proposal not accepted; the point is ongoing.");
    public IActionResult OnPostDecide(long id, long revisionId, bool approve, string? remarks, long rv) =>
        Act(id, () => workflow.DecideRevision(id, revisionId, approve, remarks, rv, User), approve ? "New due date approved." : "Request rejected.");
    public IActionResult OnPostRevise(long id, string? newDate, string? reason, long rv) => Act(id, () => workflow.ReviseDirect(id, newDate, reason, rv, User), "Due date revised.");
    public IActionResult OnPostClose(long id, string? remarks, string? authority, long rv) => Act(id, () => workflow.Close(id, remarks, authority, rv, User), "Action point closed.");
    public IActionResult OnPostDrop(long id, string? remarks, string? authority, long rv) => Act(id, () => workflow.Drop(id, remarks, authority, rv, User), "Action point dropped.");
    public IActionResult OnPostMerge(long id, string? target, string? remarks, long rv) => Act(id, () => workflow.Merge(id, target, remarks, rv, User), "Action point merged.");
    public IActionResult OnPostReopen(long id, string? remarks, long rv) => Act(id, () => workflow.Reopen(id, remarks, rv, User), "Action point reopened.");
    public IActionResult OnPostReassign(long id, long primary, List<long>? support, long rv) =>
        Act(id, () => workflow.Reassign(id, primary, support ?? new(), rv, User), "Officers changed and notified.");
}
