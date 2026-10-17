using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Leads;

/// <summary>Lead attachment download: same permission as the lead itself.</summary>
public class AttachmentModel(LeadRepository leads, LeadAccess access, FileStore files, AuditService audit) : AppPage
{
    public IActionResult OnGet(long id)
    {
        var a = leads.GetAttachment(id);
        if (a == null) return NotFound();
        var l = leads.Get(a.LeadId);
        if (l == null) return NotFound();
        if (!access.CanView(l, User))
        {
            audit.Log("Lead.AccessDenied", "LeadAttachment", id, "Attempt to download a lead attachment without permission");
            return StatusCode(403);
        }
        var stream = files.OpenRead(a.StoredPath);
        if (stream == null) return NotFound();
        audit.Log("Lead.AttachmentDownloaded", "Lead", l.Id, $"{l.Reference}: {a.OriginalFileName}");
        return File(stream, a.ContentType, a.OriginalFileName);
    }
}
