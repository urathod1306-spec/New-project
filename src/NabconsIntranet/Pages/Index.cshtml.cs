using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages;

public class IndexModel(CircularRepository circulars, NoticeRepository notices, RequestWorkflow workflow, StaffRepository staff) : AppPage
{
    public List<Circular> Latest { get; private set; } = new();
    public List<Notice> Notices { get; private set; } = new();
    public long InboxCount { get; private set; }
    public long OpenRequests { get; private set; }
    public long StaffCount { get; private set; }
    public long CircularCount { get; private set; }

    public void OnGet()
    {
        Latest = circulars.Latest(User, 6);
        Notices = notices.Current();
        InboxCount = User.IsApprover() ? workflow.InboxCount(User.UserId()) : 0;
        OpenRequests = workflow.Mine(User.UserId()).Count(r => r.State != RequestStates.Closed);
        StaffCount = staff.Count();
        CircularCount = circulars.CountPublished();
    }
}
