using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.ENotes;

public class ViewModel(ENoteRepository notes, ENoteWorkflow workflow, ENoteOptions o, AuditService audit) : ENotePage
{
    public ENote Note { get; private set; } = new();
    public List<ENoteBodyVersion> Versions { get; private set; } = new();
    public ENoteBodyVersion? Shown { get; private set; }
    public List<string> Paragraphs { get; private set; } = new();
    public List<ENoteSheetEntry> Sheet { get; private set; } = new();
    public List<ENoteAttachment> Attachments { get; private set; } = new();
    public ENotePermissions Can { get; private set; } = null!;
    public long FileCount { get; private set; }
    public long FileBytes { get; private set; }
    public ENoteOptions Options => o;
    public string? Sender { get; private set; }
    public bool UnsentChanges { get; private set; }
    // "Mark to" without JavaScript: when the typed name matches several people, they are offered to choose from
    public List<ENoteLookupRow>? Candidates { get; private set; }
    public string? MarkQuery { get; private set; }
    public string? MarkRemarks { get; private set; }

    private bool Load(long id, long? v = null)
    {
        var n = notes.Get(id);
        if (n == null || !notes.CanView(n, User)) return false;
        Note = n;
        Can = workflow.Permissions(n, User);
        Versions = notes.Versions(id);
        Shown = v is > 0 ? Versions.FirstOrDefault(x => x.Version == v) ?? Versions.LastOrDefault() : Versions.LastOrDefault();
        Paragraphs = ENoteRepository.Paragraphs(n.State == ENoteStates.Draft ? n.DraftBody : Shown?.BodyText);
        UnsentChanges = n.State == ENoteStates.Returned && Can.IsInitiator && (n.DraftBody ?? "").Trim() != (Versions.LastOrDefault()?.BodyText ?? "");
        Sheet = notes.Sheet(id);
        Attachments = notes.Attachments(id);
        (FileCount, FileBytes) = notes.Usage(id);
        if (n.PreviousHolderUserId is long p && n.State is ENoteStates.InMovement or ENoteStates.Returned) Sender = notes.Person(p)?.Name;
        return true;
    }

    public IActionResult OnGet(long id, long? v)
    {
        if (!Load(id, v)) return NotFound();
        if (notes.FirstViewToday(id, User.UserId())) audit.Log("ENote.Viewed", "ENote", id, Note.DisplayNumber);
        return Page();
    }

    private long Rv => long.TryParse(Request.Form["rowVersion"], out var rv) ? rv : 0;
    private string? Remarks => Request.Form["remarks"];

    private IActionResult Act(long id, Action action, string done)
    {
        var n = notes.Get(id);
        if (n == null || !notes.CanView(n, User)) return NotFound();
        try { action(); Flash(done); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return Redirect($"/ENotes/{id}");
    }

    public IActionResult OnPostMark(long id)
    {
        var n = notes.Get(id);
        if (n == null || !notes.CanView(n, User)) return NotFound();
        var query = Clean(Request.Form["toQuery"], 100);
        long to = long.TryParse(Request.Form["toUserId"], out var t) ? t : 0;
        if (to == 0)
        {
            var matches = notes.Lookup(query, User.UserId());
            var exact = matches.Where(m => string.Equals(m.Name, query, StringComparison.OrdinalIgnoreCase) || string.Equals(m.Username, query, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) to = exact[0].Id;
            else if (matches.Count == 1) to = matches[0].Id;
            else
            {
                Load(id);
                MarkQuery = query; MarkRemarks = Remarks;
                Candidates = matches;
                if (matches.Count == 0) FlashErrorNow(query == null || query.Length < 2 ? "Type at least 2 letters of the person's name." : $"Nobody active matches “{query}”.");
                return Page();
            }
        }
        var name = notes.Person(to)?.Name ?? "the selected person";
        return Act(id, () => workflow.MarkTo(id, to, Remarks, Rv, User), n.State == ENoteStates.Draft
            ? $"Submitted and marked to {name}. The note number has been assigned." : $"Marked to {name}.");
    }

    private void FlashErrorNow(string message) => ViewData["NowError"] = message;

    public IActionResult OnPostComment(long id) => Act(id, () => workflow.Comment(id, Request.Form["text"], Rv, User), "Comment added to the note-sheet.");
    public IActionResult OnPostSendBack(long id) => Act(id, () => workflow.SendBack(id, Remarks, Rv, User), "Sent back.");
    public IActionResult OnPostReturn(long id) => Act(id, () => workflow.ReturnToInitiator(id, Remarks, Rv, User), "Returned to the initiator.");
    public IActionResult OnPostApprove(long id) => Act(id, () => workflow.Decide(id, true, Remarks, Rv, User), "Approved. The note goes back to its initiator.");
    public IActionResult OnPostReject(long id) => Act(id, () => workflow.Decide(id, false, Remarks, Rv, User), "Rejected. The note goes back to its initiator.");
    public IActionResult OnPostWithdraw(long id) => Act(id, () => workflow.Withdraw(id, Request.Form["reason"], Rv, User), "The note has been withdrawn.");
    public IActionResult OnPostClose(long id) => Act(id, () => workflow.Close(id, Rv, User), "The note is closed and filed. It can no longer be changed.");
    public IActionResult OnPostRemoveFile(long id, long attachmentId) => Act(id, () => workflow.RemoveDraftAttachment(id, attachmentId, User), "Attachment removed from the draft.");
    public IActionResult OnPostSupersede(long id, long attachmentId, long byId) => Act(id, () => workflow.Supersede(id, attachmentId, byId, User), "Marked as superseded.");

    public async Task<IActionResult> OnPostAttachAsync(long id, List<IFormFile> uploads, long? replacesId)
    {
        var n = notes.Get(id);
        if (n == null || !notes.CanView(n, User)) return NotFound();
        try
        {
            var k = await workflow.AttachAsync(id, uploads, replacesId is > 0 ? replacesId : null, User);
            Flash(k == 1 ? "Attachment added." : $"{k} attachments added.");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return Redirect($"/ENotes/{id}");
    }

    public string Days(string? iso) { var d = HttpContext.RequestServices.GetRequiredService<Fmt>().DaysSince(iso); return d < 1 ? "today" : d == 1 ? "1 day" : $"{d:0} days"; }

    public static string MovementText(ENoteSheetEntry e)
    {
        string Who() => e.ToName + (string.IsNullOrEmpty(e.ToDesignation) ? "" : $" ({e.ToDesignation})");
        return e.Action switch
        {
            "Created" => "Draft created",
            "Submitted" => $"Submitted and marked to {Who()}",
            "Resubmitted" => $"Revised, re-submitted and marked to {Who()}",
            "MarkedTo" => $"Marked to {Who()}",
            "SentBack" => e.ToState == ENoteStates.Returned ? $"Sent back to the initiator, {Who()}" : $"Sent back to {Who()}",
            "ReturnedToInitiator" => $"Returned to the initiator, {Who()}",
            ENoteStates.Approved => "Approved",
            ENoteStates.Rejected => "Rejected",
            "Withdrawn" => "Withdrawn by the initiator",
            "Closed" => "Closed / filed by the initiator",
            _ => e.Action ?? "",
        };
    }
}
