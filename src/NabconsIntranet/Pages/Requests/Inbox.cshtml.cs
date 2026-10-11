using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Requests;

public class InboxModel(RequestWorkflow workflow) : AppPage
{
    public List<ApprovalRequest> Pending { get; private set; } = new();
    public List<ApprovalRequest> Actioned { get; private set; } = new();

    public void OnGet()
    {
        Pending = workflow.Inbox(User.UserId());
        Actioned = workflow.ActionedBy(User.UserId());
    }
}
