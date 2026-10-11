using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Requests;

public class IndexModel(RequestWorkflow workflow) : AppPage
{
    public List<ApprovalRequest> Items { get; private set; } = new();
    public void OnGet() => Items = workflow.Mine(User.UserId());
}
