using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Account;

public class LoginModel(UserRepository users, AuditService audit, IntranetOptions o) : AppPage
{
    [BindProperty, Required, StringLength(100)] public string Username { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password), StringLength(200)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }

    public IActionResult OnGet(string? returnUrl)
    {
        if (o.IsWindowsAuth) return Redirect("/");
        ReturnUrl = returnUrl;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (o.IsWindowsAuth) return Redirect("/");
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return Page();

        var u = users.GetByUsername(Username.Trim());
        const string generic = "The user name or password is incorrect.";
        if (u == null)
        {
            audit.Log("Login.Failed", "User", null, $"Unknown user name '{Clean(Username, 60)}'", "anonymous");
            ModelState.AddModelError("", generic);
            return Page();
        }
        if (!u.IsActive)
        {
            audit.Log("Login.Failed", "User", u.Id, "Account is deactivated", u.Username);
            ModelState.AddModelError("", generic);
            return Page();
        }
        if (u.IsLockedOut)
        {
            audit.Log("Login.Blocked", "User", u.Id, "Attempt while account locked", u.Username);
            ModelState.AddModelError("", $"This account is temporarily locked after repeated failed attempts. Try again after {o.LockoutMinutes} minutes or ask the administrator to unlock it.");
            return Page();
        }
        if (!PasswordHasher.Verify(Password, u.PasswordHash))
        {
            users.RecordFailedLogin(u, o.MaxFailedLogins, o.LockoutMinutes);
            var locked = u.IsLockedOut;
            audit.Log(locked ? "Login.LockedOut" : "Login.Failed", "User", u.Id, locked ? $"Locked for {o.LockoutMinutes} minutes" : "Wrong password", u.Username);
            ModelState.AddModelError("", locked ? $"Too many failed attempts. The account is locked for {o.LockoutMinutes} minutes." : generic);
            return Page();
        }

        users.RecordSuccessfulLogin(u.Id);
        var principal = new ClaimsPrincipal(IdentityBuilder.BuildIdentity(u, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });
        audit.Log("Login.Success", "User", u.Id, null, u.Username);
        if (u.MustChangePassword) return Redirect("/Account/ChangePassword");
        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : "/");
    }
}
