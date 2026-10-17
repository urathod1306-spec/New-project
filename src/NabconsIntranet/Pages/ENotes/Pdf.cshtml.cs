using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.ENotes;

/// <summary>Archival PDF: the initiator only, once the note is approved, rejected, withdrawn or closed.</summary>
public class PdfModel(ENoteRepository notes, ENoteWorkflow workflow, ENotePdfService pdf, AuditService audit) : ENotePage
{
    public async Task<IActionResult> OnGetAsync(long id)
    {
        var n = notes.Get(id);
        if (n == null || !notes.CanView(n, User) || !workflow.Permissions(n, User).CanPdf) return NotFound();
        var (full, sha) = await pdf.GetOrCreateAsync(id);
        audit.Log("ENote.PdfDownloaded", "ENote", id, $"{n.DisplayNumber}: PDF SHA-256 {sha}");
        var name = new string((n.NoteNumber ?? "e-note").Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_').ToArray()) + ".pdf";
        return PhysicalFile(full, "application/pdf", name);
    }
}
