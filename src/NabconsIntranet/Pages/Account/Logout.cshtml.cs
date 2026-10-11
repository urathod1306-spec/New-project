using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Account;

public class LogoutModel(AuditService audit, IntranetOptions o) : AppPage
{
    public IActionResult OnGet() => Redirect("/");

    public async Task<IActionResult> OnPostAsync()
    {
        if (o.IsWindowsAuth) return Redirect("/");
        audit.Log("Logout", "User", User.UserId());
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/Account/Login");
    }
}
