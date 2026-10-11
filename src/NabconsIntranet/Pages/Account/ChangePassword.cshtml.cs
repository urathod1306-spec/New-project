using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Account;

public class ChangePasswordModel(UserRepository users, AuditService audit, IntranetOptions o) : AppPage
{
    [BindProperty, Required, DataType(DataType.Password)] public string CurrentPassword { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password)] public string NewPassword { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password)] public string ConfirmPassword { get; set; } = "";
    public bool Forced { get; private set; }

    public IActionResult OnGet()
    {
        if (o.IsWindowsAuth) return Redirect("/");
        Forced = User.HasClaim(ClaimNames.MustChangePassword, "1");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (o.IsWindowsAuth) return Redirect("/");
        Forced = User.HasClaim(ClaimNames.MustChangePassword, "1");
        if (!ModelState.IsValid) return Page();
        var u = users.Get(User.UserId());
        if (u == null) return Redirect("/Account/Login");
        if (!PasswordHasher.Verify(CurrentPassword, u.PasswordHash))
        {
            ModelState.AddModelError(nameof(CurrentPassword), "The current password is incorrect.");
            return Page();
        }
        if (NewPassword != ConfirmPassword)
        {
            ModelState.AddModelError(nameof(ConfirmPassword), "The new passwords do not match.");
            return Page();
        }
        if (NewPassword == CurrentPassword)
        {
            ModelState.AddModelError(nameof(NewPassword), "Choose a password different from the current one.");
            return Page();
        }
        var policy = PasswordHasher.CheckPolicy(NewPassword, u.Username);
        if (policy != null)
        {
            ModelState.AddModelError(nameof(NewPassword), policy);
            return Page();
        }
        users.SetPassword(u.Id, NewPassword, mustChange: false, u.Username);
        audit.Log("User.PasswordChanged", "User", u.Id);
        // sign in again with the new security stamp
        u = users.Get(u.Id)!;
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(IdentityBuilder.BuildIdentity(u, CookieAuthenticationDefaults.AuthenticationScheme)));
        Flash("Your password has been changed.");
        return Redirect("/");
    }
}
