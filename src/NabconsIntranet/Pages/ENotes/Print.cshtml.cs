using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.ENotes;

/// <summary>Print-friendly HTML (no navigation): a light alternative to the PDF for anyone who may see the note.</summary>
public class PrintModel(ENoteRepository notes) : ENotePage
{
    public ENote Note { get; private set; } = new();
    public ENoteBodyVersion? Body { get; private set; }
    public List<string> Paragraphs { get; private set; } = new();
    public List<ENoteSheetEntry> Sheet { get; private set; } = new();
    public List<ENoteAttachment> Attachments { get; private set; } = new();

    public IActionResult OnGet(long id)
    {
        var n = notes.Get(id);
        if (n == null || !n.WasSubmitted || !notes.CanView(n, User)) return NotFound();
        Note = n;
        Body = notes.Version(id, null);
        Paragraphs = ENoteRepository.Paragraphs(Body?.BodyText);
        Sheet = notes.Sheet(id);
        Attachments = notes.Attachments(id);
        return Page();
    }
}
