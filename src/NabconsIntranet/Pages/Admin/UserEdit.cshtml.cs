using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

public class UserEditModel(UserRepository users, MasterRepository masters, AuditService audit, IntranetOptions o,
    ProfileRepository profiles, StaffRepository staff, PhotoService photos, ComplianceWorkflow compliance) : AppPage
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
        var vertical = Clean(Input.Vertical, 100);
        if (vertical != null && !Verticals.Contains(vertical, StringComparer.OrdinalIgnoreCase) && vertical != Existing?.Vertical)
            ModelState.AddModelError("Input.Vertical", "Choose a vertical from the list (Administration → Masters).");
        if (!ModelState.IsValid) return Page();

        if (Existing == null)
        {
            var u = new AppUser
            {
                Username = Input.Username.Trim(), DisplayName = Input.DisplayName.Trim(), Email = Clean(Input.Email, 150),
                Vertical = vertical, Roles = Infrastructure.Roles.Join(roles), IsActive = Input.IsActive,
                PasswordHash = o.IsWindowsAuth ? null : PasswordHasher.Hash(Input.TempPassword!), MustChangePassword = !o.IsWindowsAuth,
            };
            var newId = users.Create(u, User.UserName());
            audit.Log("User.Created", "User", newId, $"{u.Username}; roles={u.Roles}; active={u.IsActive}");
            if (roles.Contains(Infrastructure.Roles.Auditor)) compliance.NotifyAuditorGranted(newId, User);
            Flash($"User {u.Username} created." + (o.IsWindowsAuth ? "" : " They must change the temporary password at first sign-in."));
            return RedirectToPage("/Admin/Users");
        }

        var before = $"roles={Existing.Roles}; active={Existing.IsActive}; vertical={Existing.Vertical}";
        var rolesChanged = Infrastructure.Roles.Join(roles) != Infrastructure.Roles.Join(Existing.RoleList) || Existing.IsActive != Input.IsActive;
        var gainsAuditor = roles.Contains(Infrastructure.Roles.Auditor) && !Existing.RoleList.Contains(Infrastructure.Roles.Auditor);
        Existing.DisplayName = Input.DisplayName.Trim();
        Existing.Email = Clean(Input.Email, 150);
        var oldVertical = Existing.Vertical;
        Existing.Roles = Infrastructure.Roles.Join(roles);
        Existing.IsActive = Input.IsActive;
        users.Update(Existing, User.UserName(), rotateStamp: rolesChanged);
        compliance.UserChanged();          // a deactivated Compliance Admin loses access at once
        // the vertical changes with the linked directory entry, and is recorded in the profile history
        profiles.SetVerticalByAdmin(Existing.Id, Existing.StaffId, oldVertical, vertical, User.UserName());
        Existing.Vertical = vertical;
        var after = $"roles={Existing.Roles}; active={Existing.IsActive}; vertical={Existing.Vertical}";
        audit.Log(rolesChanged ? "User.RolesChanged" : "User.Updated", "User", Existing.Id, $"{Existing.Username}: {before} -> {after}");
        if (!o.IsWindowsAuth && !string.IsNullOrEmpty(Input.TempPassword))
        {
            users.SetPassword(Existing.Id, Input.TempPassword, mustChange: true, User.UserName());
            audit.Log("User.PasswordReset", "User", Existing.Id, $"{Existing.Username}: temporary password set by administrator");
            compliance.NotifyMemberPasswordReset(Existing.Id, User);      // the Compliance Cell is told when a member's password is reset
        }
        if (gainsAuditor) compliance.NotifyAuditorGranted(Existing.Id, User);
        Flash($"User {Existing.Username} updated." + (rolesChanged ? " They will need to sign in again." : ""));
        return RedirectToPage("/Admin/Users");
    }

    public List<StaffMember> StaffOptions => staff.All();
    public bool HasPhoto => Existing != null && photos.Exists(Existing.Id);

    public IActionResult OnPostLink(long id, long? staffId)
    {
        if (users.Get(id) == null) return NotFound();
        try
        {
            profiles.SetLink(id, staffId is > 0 ? staffId : null, User.UserName());
            Flash(staffId is > 0 ? "Account linked to the staff directory entry." : "Directory link removed.");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostRemovePhoto(long id)
    {
        var u = users.Get(id);
        if (u == null) return NotFound();
        photos.Delete(id);
        users.BumpPhotoVersion(id, removed: true);
        audit.Log("Profile.PhotoRemovedByAdmin", "User", id, $"Photo of {u.Username} removed by administrator");
        Flash($"Photo of {u.DisplayName} removed.");
        return RedirectToPage(new { id });
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
