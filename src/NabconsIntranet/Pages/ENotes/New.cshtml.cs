using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.ENotes;

public class NewModel(ENoteWorkflow workflow, ENoteOptions o, MasterRepository masters) : ENotePage
{
    public ENoteFormView View { get; private set; } = null!;

    private void Build(ENoteForm f, string? error) => View = new ENoteFormView(f, masters.Names(MasterTypes.ENoteCategory), o.MaxBodyChars,
        ConfidentialLocked: false, ShowDeclaration: true, ShowFiles: true, string.Join(",", o.AllowedExtensions), o.MaxFileMB, error);

    public void OnGet() => Build(new ENoteForm(), null);

    public async Task<IActionResult> OnPostAsync(List<IFormFile> uploads)
    {
        var f = new ENoteForm
        {
            Subject = Request.Form["Subject"], Category = Request.Form["Category"], Body = Request.Form["Body"],
            Confidential = Request.Form["Confidential"] == "true", Declaration = Request.Form["Declaration"] == "true",
        };
        long id;
        try { id = workflow.Create(new ENoteWorkflow.DraftInput(f.Subject, f.Category, f.Body, f.Confidential, f.Declaration), User); }
        catch (WorkflowException ex) { Build(f, ex.Message); return Page(); }
        var files = uploads.Where(u => u.Length > 0).ToList();
        if (files.Count > 0)
        {
            try { await workflow.AttachAsync(id, files, null, User); }
            catch (WorkflowException ex) { FlashError("The draft was saved, but the attachments were not: " + ex.Message); return Redirect($"/ENotes/{id}"); }
        }
        Flash("Draft saved. Check it, then mark it to the first person in the chain.");
        return Redirect($"/ENotes/{id}");
    }
}
