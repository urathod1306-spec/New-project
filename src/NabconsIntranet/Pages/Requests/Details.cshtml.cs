using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Requests;

public class DetailsModel(RequestWorkflow workflow) : AppPage
{
    public ApprovalRequest Item { get; private set; } = new();
    public List<ApprovalAction> History { get; private set; } = new();
    public List<Attachment> Attachments { get; private set; } = new();
    public List<WorkflowAction> Actions { get; private set; } = new();
    public bool CanEdit { get; private set; }

    private bool Load(long id)
    {
        var r = workflow.Get(id);
        if (r == null || !workflow.CanView(r, User)) return false;
        Item = r;
        History = workflow.History(id);
        Attachments = workflow.Attachments(id);
        Actions = workflow.AllowedActions(r, User);
        CanEdit = workflow.CanEdit(r, User);
        return true;
    }

    public IActionResult OnGet(long id) => Load(id) ? Page() : NotFound();

    public IActionResult OnPost(long id, string wfAction, string? remarks, long rowVersion)
    {
        var r = workflow.Get(id);
        if (r == null || !workflow.CanView(r, User)) return NotFound();
        try
        {
            workflow.Execute(id, wfAction, remarks, rowVersion, User);
            var after = workflow.Get(id)!;
            Flash($"{after.RequestNumber}: {wfAction} recorded. Status is now “{after.State}”.");
            return RedirectToPage(new { id });
        }
        catch (WorkflowException ex)
        {
            FlashError(ex.Message);
            return RedirectToPage(new { id });
        }
    }
}
