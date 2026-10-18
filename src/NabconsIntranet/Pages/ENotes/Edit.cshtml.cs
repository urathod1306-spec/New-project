using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.ENotes;

public class EditModel(ENoteRepository notes, ENoteWorkflow workflow, ENoteOptions o, MasterRepository masters) : ENotePage
{
    public ENote Note { get; private set; } = new();
    public ENoteFormView View { get; private set; } = null!;

    private bool Load(long id)
    {
        var n = notes.Get(id);
        if (n == null || !workflow.Permissions(n, User).CanEdit) return false;
        Note = n;
        return true;
    }

    private void Build(ENoteForm f, string? error) => View = new ENoteFormView(f, masters.Names(MasterTypes.ENoteCategory), o.MaxBodyChars,
        ConfidentialLocked: Note.WasSubmitted, ShowDeclaration: !Note.WasSubmitted, ShowFiles: false, "", o.MaxFileMB, error);

    public IActionResult OnGet(long id)
    {
        if (!Load(id)) return NotFound();
        Build(new ENoteForm
        {
            Subject = Note.Subject, Category = Note.Category, Body = Note.DraftBody, Confidential = Note.IsConfidential,
            Declaration = Note.DeclarationAccepted, RowVersion = Note.RowVersion,
        }, null);
        return Page();
    }

    public IActionResult OnPost(long id)
    {
        if (!Load(id)) return NotFound();
        var f = new ENoteForm
        {
            Subject = Request.Form["Subject"], Category = Request.Form["Category"], Body = Request.Form["Body"],
            Confidential = Request.Form["Confidential"] == "true", Declaration = Request.Form["Declaration"] == "true",
            RowVersion = long.TryParse(Request.Form["RowVersion"], out var rv) ? rv : 0,
        };
        try
        {
            workflow.EditDraft(id, new ENoteWorkflow.DraftInput(f.Subject, f.Category, f.Body, f.Confidential, f.Declaration), f.RowVersion, User);
        }
        catch (WorkflowException ex) { Build(f, ex.Message); return Page(); }
        Flash(Note.WasSubmitted ? "Changes saved. When ready, mark the note onward — the revised text becomes a new version." : "Draft saved.");
        return Redirect($"/ENotes/{id}");
    }
}
