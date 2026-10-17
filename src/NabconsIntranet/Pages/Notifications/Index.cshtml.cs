using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Notifications;

public class IndexModel(INotificationService notifications) : AppPage
{
    public NabconsIntranet.Data.Paged<Notification> Result { get; private set; } = new();
    public bool UnreadOnly { get; private set; }
    public string? Module { get; private set; }

    public void OnGet(string? show, string? module, int page = 1)
    {
        UnreadOnly = show == "unread";
        Module = module != null && NotificationModules.Labels.ContainsKey(module) ? module : null;
        Result = notifications.Page(User.UserId(), UnreadOnly, Module, page);
    }

    public string PageUrl(int page) => QueryUrl(UnreadOnly ? "unread" : null, Module, page);

    public static string QueryUrl(string? show, string? module, int page = 1)
    {
        var q = new List<string>();
        if (show != null) q.Add("show=" + show);
        if (module != null) q.Add("module=" + Uri.EscapeDataString(module));
        if (page > 1) q.Add("page=" + page);
        return "/Notifications" + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    /// <summary>The bell drop-down's list (latest 10), loaded when the bell is opened.</summary>
    public IActionResult OnGetMenu() => Partial("_NotificationMenu", notifications.Latest(User.UserId(), 10));

    public IActionResult OnPostMarkAllRead(string? returnUrl)
    {
        var n = notifications.MarkAllRead(User.UserId());
        Flash(n == 0 ? "No unread notifications." : $"{n} notification(s) marked as read.");
        return LocalRedirect(Url.IsLocalUrl(returnUrl) && NotificationService.IsSafeLink(returnUrl) ? returnUrl! : "/Notifications");
    }
}
