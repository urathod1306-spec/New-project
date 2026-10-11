using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

public class UserEditModel(UserRepository users, MasterRepository masters, AuditService audit, IntranetOptions o) : AppPage
{
    public sealed class InputModel
    {
        [Required, StringLength(100), RegularExpression(@"^[A-Za-z0-9._\-\\@]+$", ErrorMessage = "Use letters, digits and . _ - \\ @ only."), Display(Name = "User name")]
        public string Username { get; set; } = "";
        [Required, StringLength(120), Display(Name = "Display name")] public string DisplayName { get; set; } = "";
        [EmailAddress, StringLength(150)] public string? Email { get; set; }
        public string? Vertical { get; set; }
        public List<string> Roles { get; set; } = new();
        [Display(Name = "Account active")] public bool IsActive { get; set; } = true;
        [DataType(DataType.Password), Display(Name = "Temporary password")] public string? TempPassword { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public long Id { get; private set; }
    public AppUser? Existing { get; private set; }
    public List<string> Verticals { get; private set; } = new();
    public bool WindowsMode => o.IsWindowsAuth;

    public IActionResult OnGet(long? id)
    {
        Id = id ?? 0;
        Verticals = masters.Names(MasterTypes.Vertical);
        if (id != null)
        {
            Existing = users.Get(id.Value);
            if (Existing == null) return NotFound();
            Input = new InputModel
            {
                Username = Existing.Username, DisplayName = Existing.DisplayName, Email = Existing.Email,
                Vertical = Existing.Vertical, Roles = Existing.RoleList, IsActive = Existing.IsActive,
            };
        }
        else Input.Roles = new() { Infrastructure.Roles.Staff };
        return Page();
    }

    public IActionResult OnPost(long? id)
    {
        Id = id ?? 0;
        Verticals = masters.Names(MasterTypes.Vertical);
        if (id != null)
        {
            Existing = users.Get(id.Value);
            if (Existing == null) return NotFound();
        }
        var roles = Infrastructure.Roles.Parse(string.Join(",", Input.Roles));
        var isSelf = Existing?.Id == User.UserId();
        if (isSelf && (!roles.Contains(Infrastructure.Roles.Administrator) || !Input.IsActive))
            ModelState.AddModelError("", "You cannot remove your own Administrator role or deactivate your own account.");
        if (Existing == null && users.GetByUsername(Input.Username.Trim()) != null)
            ModelState.AddModelError("Input.Username", "This user name already exists.");
        if (!o.IsWindowsAuth && Existing == null && string.IsNullOrEmpty(Input.TempPassword))
            ModelState.AddModelError("Input.TempPassword", "Set a temporary password for the new user.");
        if (!o.IsWindowsAuth && !string.IsNullOrEmpty(Input.TempPassword) && PasswordHasher.CheckPolicy(Input.TempPassword, Input.Username) is string err)
            ModelState.AddModelError("Input.TempPassword", err);
        if (!ModelState.IsValid) return Page();

        if (Existing == null)
        {
            var u = new AppUser
            {
                Username = Input.Username.Trim(), DisplayName = Input.DisplayName.Trim(), Email = Clean(Input.Email, 150),
                Vertical = Clean(Input.Vertical, 100), Roles = Infrastructure.Roles.Join(roles), IsActive = Input.IsActive,
                PasswordHash = o.IsWindowsAuth ? null : PasswordHasher.Hash(Input.TempPassword!), MustChangePassword = !o.IsWindowsAuth,
            };
            var newId = users.Create(u, User.UserName());
            audit.Log("User.Created", "User", newId, $"{u.Username}; roles={u.Roles}; active={u.IsActive}");
            Flash($"User {u.Username} created." + (o.IsWindowsAuth ? "" : " They must change the temporary password at first sign-in."));
            return RedirectToPage("/Admin/Users");
        }

        var before = $"roles={Existing.Roles}; active={Existing.IsActive}; vertical={Existing.Vertical}";
        var rolesChanged = Infrastructure.Roles.Join(roles) != Infrastructure.Roles.Join(Existing.RoleList) || Existing.IsActive != Input.IsActive;
        Existing.DisplayName = Input.DisplayName.Trim();
        Existing.Email = Clean(Input.Email, 150);
        Existing.Vertical = Clean(Input.Vertical, 100);
        Existing.Roles = Infrastructure.Roles.Join(roles);
        Existing.IsActive = Input.IsActive;
        users.Update(Existing, User.UserName(), rotateStamp: rolesChanged);
        var after = $"roles={Existing.Roles}; active={Existing.IsActive}; vertical={Existing.Vertical}";
        audit.Log(rolesChanged ? "User.RolesChanged" : "User.Updated", "User", Existing.Id, $"{Existing.Username}: {before} -> {after}");
        if (!o.IsWindowsAuth && !string.IsNullOrEmpty(Input.TempPassword))
        {
            users.SetPassword(Existing.Id, Input.TempPassword, mustChange: true, User.UserName());
            audit.Log("User.PasswordReset", "User", Existing.Id, $"{Existing.Username}: temporary password set by administrator");
        }
        Flash($"User {Existing.Username} updated." + (rolesChanged ? " They will need to sign in again." : ""));
        return RedirectToPage("/Admin/Users");
    }

    public IActionResult OnPostUnlock(long id)
    {
        var u = users.Get(id);
        if (u == null) return NotFound();
        users.Unlock(id, User.UserName());
        audit.Log("User.Unlocked", "User", id, u.Username);
        Flash($"{u.Username} unlocked.");
        return RedirectToPage(new { id });
    }
}
