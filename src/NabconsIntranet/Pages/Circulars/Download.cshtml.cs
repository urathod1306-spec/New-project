using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Circulars;

/// <summary>The only way to read a circular file: checks visibility, then streams it as a download.</summary>
public class DownloadModel(CircularRepository circulars, FileStore files, AuditService audit) : AppPage
{
    public IActionResult OnGet(long id)
    {
        var f = circulars.GetFile(id);
        if (f == null) return NotFound();
        var c = circulars.GetVisible(f.CircularId, User);
        if (c == null) return NotFound();
        var stream = files.OpenRead(f.StoredPath);
        if (stream == null) return NotFound();
        audit.Log("Circular.Downloaded", "Circular", c.Id, $"{c.CircularNumber} v{f.Version}");
        return File(stream, f.ContentType, f.OriginalFileName);
    }
}
