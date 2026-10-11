using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Requests;

public class AttachmentModel(RequestWorkflow workflow, FileStore files, AuditService audit) : AppPage
{
    public IActionResult OnGet(long id)
    {
        var a = workflow.GetAttachment(id);
        if (a == null) return NotFound();
        var r = workflow.Get(a.RequestId);
        if (r == null || !workflow.CanView(r, User)) return NotFound();
        var stream = files.OpenRead(a.StoredPath);
        if (stream == null) return NotFound();
        audit.Log("Request.AttachmentDownloaded", "Request", r.Id, a.OriginalFileName);
        return File(stream, a.ContentType, a.OriginalFileName);
    }
}
