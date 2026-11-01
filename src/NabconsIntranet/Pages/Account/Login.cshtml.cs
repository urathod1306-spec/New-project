using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Account;

public class LoginModel(UserRepository users, RegistrationRepository registrations, AuditService audit, IntranetOptions o) : AppPage
{
    [BindProperty, Required, StringLength(100)] public string Username { get; set; } = "";
    [BindProperty, Required, DataType(DataType.Password), StringLength(200)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
    private static readonly string DummyHash = PasswordHasher.Hash(Guid.NewGuid().ToString());

    public IActionResult OnGet(string? returnUrl)
    {
        if (o.IsWindowsAuth) return Redirect("/");
        ReturnUrl = returnUrl;
        return Page();
    }

    private static readonly TimeSpan IpWindow = TimeSpan.FromMinutes(10);

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (o.IsWindowsAuth) return Redirect("/");
        ReturnUrl = returnUrl;
        if (!ModelState.IsValid) return Page();
        // at most 30 failed sign-ins per computer in 10 minutes (successful sign-ins do not count)
        var ipKey = "login-failed:" + (HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        if (Throttle.Blocked(ipKey, 30, IpWindow))
        {
            ModelState.AddModelError("", "Too many sign-in attempts from this computer. Please wait a few minutes and try again.");
            return Page();
        }

        // Self-registered staff sign in with their mobile number; accept it with spaces or +91.
        var login = Username.Trim();
        var mobile = RegistrationRepository.Normalize(login);
        if (mobile != null && login.All(ch => char.IsDigit(ch) || ch is ' ' or '+' or '-')) login = mobile;
        var u = users.GetByUsername(login);
        const string generic = "The user name or password is incorrect.";
        if (u == null && mobile != null && o.SelfRegistrationAvailable)
        {
            var reg = registrations.Latest(mobile);
            if (reg?.Status == RegistrationStatus.Pending && PasswordHasher.Verify(Password, reg.PasswordHash))
            {
                ModelState.AddModelError("", "Your registration has been received and is awaiting approval by the administrator. You can sign in once it is approved.");
                return Page();
            }
            if (reg?.Status == RegistrationStatus.Rejected)
            {
                ModelState.AddModelError("", "A registration for this mobile number was not approved. Please contact the intranet administrator.");
                return Page();
            }
        }
        if (u == null || !u.IsActive) PasswordHasher.Verify(Password, DummyHash);   // same time as a real check: no hint which names exist
        if (u == null)
        {
            audit.Log("Login.Failed", "User", null, $"Unknown user name '{Clean(Username, 60)}'", "anonymous");
            Throttle.Record(ipKey, IpWindow); ModelState.AddModelError("", generic);
            return Page();
        }
        if (!u.IsActive)
        {
            audit.Log("Login.Failed", "User", u.Id, "Account is deactivated", u.Username);
            Throttle.Record(ipKey, IpWindow); ModelState.AddModelError("", generic);
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
            Throttle.Record(ipKey, IpWindow); ModelState.AddModelError("", locked ? $"Too many failed attempts. The account is locked for {o.LockoutMinutes} minutes." : generic);
            return Page();
        }

        users.RecordSuccessfulLogin(u.Id);
        var principal = new ClaimsPrincipal(IdentityBuilder.BuildIdentity(u, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, new AuthenticationProperties { IsPersistent = false });
        audit.Log("Login.Success", "User", u.Id, null, u.Username);
        if (u.MustChangePassword) return Redirect("/Account/ChangePassword");
        if (Url.IsLocalUrl(returnUrl) && returnUrl != "/") return LocalRedirect(returnUrl!);
        return LocalRedirect(u.OpenDeskAfterSignIn ? "/Desk" : "/");
    }
}
