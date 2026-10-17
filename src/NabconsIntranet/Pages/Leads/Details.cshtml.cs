using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Leads;

/// <summary>Lead detail and timeline. Every handler re-checks permission on the server.</summary>
public class DetailsModel(LeadRepository leads, LeadAccess access, MasterRepository masters, FileStore files, AuditService audit, IntranetOptions o) : AppPage
{
    public Lead Item { get; private set; } = new();
    public List<LeadEvent> Timeline { get; private set; } = new();
    public List<LeadAttachment> Files { get; private set; } = new();
    public bool CanManage { get; private set; }
    public bool CanFollowUp { get; private set; }
    public List<AppUser> BduMembers { get; private set; } = new();
    public List<string> LossReasons { get; private set; } = new();
    public string Accept => string.Join(",", FileStore.LeadExtensions);

    /// <summary>null = allowed and loaded; otherwise the result to return (404 / 403).</summary>
    private IActionResult? Load(long id)
    {
        var l = leads.Get(id);
        if (l == null) return NotFound();
        if (!access.CanView(l, User))
        {
            audit.Log("Lead.AccessDenied", "Lead", id, "Attempt to open a lead without permission");
            return StatusCode(403);
        }
        Item = l;
        Timeline = leads.Timeline(id);
        Files = leads.Attachments(id);
        CanManage = access.CanManage(User);
        CanFollowUp = access.CanSubmitterFollowUp(l, User);
        if (CanManage)
        {
            BduMembers = leads.BduMembers();
            LossReasons = masters.Names(MasterTypes.LeadLossReason);
        }
        return null;
    }

    public IActionResult OnGet(long id) => Load(id) ?? Page();

    public IActionResult OnPostStatus(long id, string newStatus, string? remarks, string? lossReason, string? lossReasonText,
        decimal? awardedValueLakh, DateOnly? awardDate, long rowVersion)
    {
        if (Load(id) is { } denied) return denied;
        if (!CanManage) return StatusCode(403);
        if (lossReason != null && !LossReasons.Contains(lossReason)) lossReason = null;
        try
        {
            leads.ChangeStatus(Item, new LeadUpdate(newStatus ?? "", remarks ?? "", lossReason, Clean(lossReasonText, 500),
                awardedValueLakh.HasValue ? (double)awardedValueLakh.Value : null, IsoDate(awardDate)), rowVersion, User);
            Flash($"Status changed to “{newStatus}”.");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostOwner(long id, long? ownerUserId, long rowVersion)
    {
        if (Load(id) is { } denied) return denied;
        if (!CanManage) return StatusCode(403);
        try
        {
            leads.AssignOwner(Item, ownerUserId is > 0 ? ownerUserId : null, rowVersion, User);
            Flash("Lead owner updated.");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage(new { id });
    }

    public async Task<IActionResult> OnPostNoteAsync(long id, string? note, List<IFormFile>? uploads)
    {
        if (Load(id) is { } denied) return denied;
        if (!CanManage && !CanFollowUp) return StatusCode(403);
        note = Clean(note, 4000);
        var incoming = (uploads ?? new()).Where(f => f.Length > 0 || !string.IsNullOrEmpty(f.FileName)).ToList();
        if (note == null && incoming.Count == 0) { FlashError("Write a note or choose a file."); return RedirectToPage(new { id }); }
        if (leads.AttachmentBytes(id) + incoming.Sum(f => f.Length) > o.MaxLeadTotalMB * 1024L * 1024L)
        { FlashError($"Attachments may total at most {o.MaxLeadTotalMB} MB per lead."); return RedirectToPage(new { id }); }
        var stored = new List<StoredFile>();
        foreach (var f in incoming)
        {
            try { stored.Add(await files.SaveAsync(f, "leads", FileStore.LeadExtensions, o.MaxLeadFileMB * 1024L * 1024L)); }
            catch (UploadRejectedException ex)
            {
                audit.Log("Upload.Rejected", "Lead", id, $"{f.FileName}: {ex.Message}");
                FlashError($"{f.FileName}: {ex.Message}");
                return RedirectToPage(new { id });
            }
        }
        var side = CanManage ? "BDU" : "Submitter";
        if (note != null) leads.AddNote(Item, note, User, CanManage ? "Remark" : "Note");
        foreach (var s in stored) leads.AddAttachment(Item, s, side, User);
        Flash(stored.Count > 0 ? $"{stored.Count} file(s) added." : "Added to the timeline.");
        return RedirectToPage(new { id });
    }

    public static string EventLabel(LeadEvent e) => e.EventType switch
    {
        "Submitted" => "Lead submitted",
        "StatusChanged" => $"Status: {e.FromStatus} → {e.ToStatus}",
        "OwnerChanged" => "Owner changed",
        "Attachment" => "File added",
        "Remark" => "BDU remark",
        "Note" => "Follow-up note from submitter",
        _ => e.EventType,
    };
}
