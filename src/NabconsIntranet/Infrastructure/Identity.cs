using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using NabconsIntranet.Data;

namespace NabconsIntranet.Infrastructure;

public static class IdentityBuilder
{
    public static ClaimsIdentity BuildIdentity(AppUser u, string authType, IEnumerable<string>? extraRoles = null)
    {
        var id = new ClaimsIdentity(authType, ClaimTypes.Name, ClaimTypes.Role);
        id.AddClaim(new Claim(ClaimTypes.Name, u.Username));
        id.AddClaim(new Claim(ClaimNames.UserId, u.Id.ToString()));
        id.AddClaim(new Claim(ClaimNames.DisplayName, u.DisplayName));
        id.AddClaim(new Claim(ClaimNames.Stamp, u.SecurityStamp));
        if (!string.IsNullOrEmpty(u.Vertical)) id.AddClaim(new Claim(ClaimNames.Vertical, u.Vertical));
        if (u.MustChangePassword) id.AddClaim(new Claim(ClaimNames.MustChangePassword, "1"));
        foreach (var r in u.RoleList.Concat(extraRoles ?? Array.Empty<string>()).Distinct())
            id.AddClaim(new Claim(ClaimTypes.Role, r));
        return id;
    }

    /// <summary>Local accounts: every request re-checks the account is still active and unchanged.</summary>
    public static async Task ValidateCookie(CookieValidatePrincipalContext ctx)
    {
        var users = ctx.HttpContext.RequestServices.GetRequiredService<UserRepository>();
        var uid = ctx.Principal?.UserId() ?? 0;
        var stamp = ctx.Principal?.FindFirst(ClaimNames.Stamp)?.Value;
        var u = uid > 0 ? users.Get(uid) : null;
        if (u == null || !u.IsActive || u.SecurityStamp != stamp)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}

/// <summary>
/// Windows Authentication: maps the signed-in Windows/AD account to an intranet user record
/// and roles. Roles come from the intranet user record (managed on the Admin → Users page),
/// from AD group mappings in appsettings.json, and from the WindowsAdministrators list.
/// </summary>
public sealed class WindowsRoleTransformation(UserRepository users, IntranetOptions o, AuditService audit) : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.HasClaim(c => c.Type == ClaimNames.UserId))
            return Task.FromResult(principal);

        var name = principal.Identity.Name!;
        var u = users.GetByUsername(name);
        if (u == null)
        {
            if (!o.WindowsAutoProvisionStaff && !IsConfiguredAdmin(name)) return Task.FromResult(principal);
            var display = name.Contains('\\') ? name[(name.IndexOf('\\') + 1)..] : name;
            u = new AppUser { Username = name, DisplayName = display, Roles = Roles.Staff, IsActive = true };
            users.Create(u, "system (Windows sign-in)");
            u = users.GetByUsername(name)!;
            audit.Log("User.AutoProvisioned", "User", u.Id, $"First Windows sign-in by {name}", name);
        }
        if (!u.IsActive) return Task.FromResult(principal);

        var extra = new List<string>();
        if (IsConfiguredAdmin(name)) extra.Add(Roles.Administrator);
        if (OperatingSystem.IsWindows()) extra.AddRange(GroupRoles(principal));

        var result = new ClaimsPrincipal(principal.Identities);
        result.AddIdentity(IdentityBuilder.BuildIdentity(u, "NabconsRoles", extra));
        return Task.FromResult(result);
    }

    private bool IsConfiguredAdmin(string name) => o.WindowsAdministrators.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

    [SupportedOSPlatform("windows")]
    private IEnumerable<string> GroupRoles(ClaimsPrincipal principal)
    {
        if (o.WindowsGroupRoles.Count == 0 || principal.Identity is not WindowsIdentity wi) yield break;
        var wp = new WindowsPrincipal(wi);
        foreach (var (group, role) in o.WindowsGroupRoles)
        {
            bool member;
            try { member = wp.IsInRole(group); } catch { member = false; }
            if (member && Roles.All.Contains(role)) yield return role;
        }
    }
}
