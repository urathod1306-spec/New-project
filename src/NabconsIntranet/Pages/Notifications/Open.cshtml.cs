using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Notifications;

/// <summary>Opens one of the signed-in user's own notifications: marks it read and goes to its (internal) link.</summary>
public class OpenModel(INotificationService notifications) : AppPage
{
    public IActionResult OnGet(long id)
    {
        var n = notifications.Open(id, User.UserId());
        if (n == null) return NotFound();
        return LocalRedirect(NotificationService.IsSafeLink(n.Link) && n.Link != null ? n.Link : "/Notifications");
    }
}
