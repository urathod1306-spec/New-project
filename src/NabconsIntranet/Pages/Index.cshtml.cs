using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages;

public class IndexModel(CircularRepository circulars, NoticeRepository notices, RequestWorkflow workflow, StaffRepository staff,
    UserRepository users, IntranetOptions o) : AppPage
{
    public List<Circular> Latest { get; private set; } = new();
    public List<Notice> Notices { get; private set; } = new();
    public long InboxCount { get; private set; }
    public long OpenRequests { get; private set; }
    public long StaffCount { get; private set; }
    public long CircularCount { get; private set; }

    public const string DeskShownCookie = "NABCONS.DeskShown";

    public IActionResult OnGet()
    {
        // Windows sign-in has no sign-in page: honour "Open My Desk after sign-in" on the first visit of each browser session.
        if (o.IsWindowsAuth && !Request.Cookies.ContainsKey(DeskShownCookie))
        {
            Response.Cookies.Append(DeskShownCookie, "1", new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Secure = Request.IsHttps, IsEssential = true });
            if (users.Get(User.UserId())?.OpenDeskAfterSignIn == true) return Redirect("/Desk");
        }
        Latest = circulars.Latest(User, 6);
        Notices = notices.Current();
        InboxCount = User.IsApprover() ? workflow.InboxCount(User.UserId()) : 0;
        OpenRequests = workflow.Mine(User.UserId()).Count(r => r.State != RequestStates.Closed);
        StaffCount = staff.Count();
        CircularCount = circulars.CountPublished();
        return Page();
    }
}
