using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Profile;

/// <summary>
/// My Profile. Every handler works only on the signed-in user's own record: the user ID always comes from the
/// sign-in cookie, never from the form, and only the contact fields listed below are ever read from the request.
/// </summary>
public class IndexModel(UserRepository users, ProfileRepository profiles, MasterRepository masters, PhotoService photos,
    AuditService audit, IntranetOptions o) : AppPage
{
    public const string OtherLocation = "__other__";

    public AppUser Me { get; private set; } = new();
    public StaffMember? Staff { get; private set; }
    public ProfileChangeRequest? PendingVertical { get; private set; }
    public List<ProfileAuditEntry> History { get; private set; } = new();
    public List<string> Verticals { get; private set; } = new();
    public List<string> Locations { get; private set; } = new();
    public bool Editing { get; private set; }
    public Dictionary<string, string> Errors { get; private set; } = new();
    public bool HasPhoto => photos.Exists(Me.Id);

    // form values (only these are ever bound)
    public string? Mobile { get; set; }
    public string? Extension { get; set; }
    public string? OfficialEmail { get; set; }
    public string? Location { get; set; }
    public string? LocationOther { get; set; }

    private bool Load()
    {
        var u = users.Get(User.UserId());
        if (u == null) return false;
        Me = u;
        Staff = profiles.StaffFor(u);
        PendingVertical = profiles.PendingVertical(u.Id);
        History = profiles.History(u.Id);
        Verticals = masters.Names(MasterTypes.Vertical);
        Locations = masters.Names(MasterTypes.Location);
        if (u.Vertical is string v && !Verticals.Contains(v)) Verticals.Insert(0, v);
        return true;
    }

    private void FillFormFromStaff()
    {
        Mobile = RegistrationRepository.Normalize(Staff?.Mobile) ?? Staff?.Mobile;
        Extension = Staff?.Extension;
        OfficialEmail = Staff?.OfficialEmail;
        if (Staff?.Location is string l && !Locations.Contains(l)) { Location = OtherLocation; LocationOther = l; }
        else Location = Staff?.Location;
    }

    public IActionResult OnGet(bool edit = false)
    {
        if (!Load()) return Redirect("/Account/Login");
        Editing = edit && Staff != null;
        FillFormFromStaff();
        return Page();
    }

    public async Task<IActionResult> OnPostContactAsync()
    {
        if (!Load()) return Redirect("/Account/Login");
        if (Staff == null) { FlashError("Your account is not linked to a staff directory entry yet. Ask the administrator."); return RedirectToPage(); }
        var f = Request.Form;
        Mobile = f["Mobile"]; Extension = f["Extension"]; OfficialEmail = f["OfficialEmail"]; Location = f["Location"]; LocationOther = f["LocationOther"];
        string? loc = Location == OtherLocation ? LocationOther : Location;
        if (Location == OtherLocation && string.IsNullOrWhiteSpace(LocationOther)) Errors["Location"] = "Type your office / location, or choose one from the list.";
        else if (Location != OtherLocation && !string.IsNullOrWhiteSpace(loc) && !Locations.Contains(loc!, StringComparer.OrdinalIgnoreCase))
            Errors["Location"] = "Choose a location from the list, or “Others” to type it.";
        var problems = profiles.Validate(Me, Staff, new ProfileContact(Mobile, Extension, OfficialEmail, loc), out var clean);
        foreach (var kv in problems) Errors[kv.Key] = kv.Value;
        if (Errors.Count > 0) { Editing = true; return Page(); }

        var changed = profiles.ApplyContact(Me, Staff, clean);
        if (changed.Contains("SignInName"))
        {
            // the sign-in name followed the new mobile number: refresh the sign-in cookie
            var fresh = users.Get(Me.Id)!;
            if (!o.IsWindowsAuth)
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(IdentityBuilder.BuildIdentity(fresh, CookieAuthenticationDefaults.AuthenticationScheme)));
            Flash($"Profile updated. You now sign in with your new mobile number {fresh.Username}.");
        }
        else Flash(changed.Count == 0 ? "No changes to save." : "Profile updated: " + string.Join(", ", changed.Select(Label)) + ".");
        return RedirectToPage();
    }

    public IActionResult OnPostVertical()
    {
        if (!Load()) return Redirect("/Account/Login");
        var requested = Request.Form["Vertical"].ToString();
        requested = string.IsNullOrWhiteSpace(requested) ? "" : requested.Trim();
        if (requested != "" && !Verticals.Contains(requested)) { FlashError("Choose a vertical from the list."); return RedirectToPage(); }
        try
        {
            if (!profiles.RequestVertical(Me, Staff, requested == "" ? null : requested)) Flash("Your vertical is unchanged.");
            else Flash(o.VerticalChangeRequiresApproval
                ? "Vertical change sent to the administrator for approval. Your access stays as it is until it is approved."
                : "Vertical updated.");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage();
    }

    public IActionResult OnPostPhoto(IFormFile? photo)
    {
        if (!Load()) return Redirect("/Account/Login");
        if (photo == null || photo.Length == 0) { FlashError("Choose a photo to upload."); return RedirectToPage(); }
        try
        {
            using (var s = photo.OpenReadStream()) photos.Save(Me.Id, s, photo.Length);
            users.BumpPhotoVersion(Me.Id);
            audit.Log("Profile.PhotoUploaded", "User", Me.Id, $"{photo.Length} bytes received, stored as 400x400 JPEG without metadata");
            Flash("Photo updated.");
        }
        catch (UploadRejectedException ex)
        {
            audit.Log("Upload.Rejected", "User", Me.Id, "Photo: " + ex.Message);
            FlashError(ex.Message);
        }
        return RedirectToPage();
    }

    public IActionResult OnPostRemovePhoto()
    {
        if (!Load()) return Redirect("/Account/Login");
        photos.Delete(Me.Id);
        users.BumpPhotoVersion(Me.Id, removed: true);
        audit.Log("Profile.PhotoRemoved", "User", Me.Id, "Removed by the user");
        Flash("Photo removed.");
        return RedirectToPage();
    }

    public static string Label(string field) => field switch
    {
        "OfficialEmail" => "official e-mail", "Extension" => "extension", "Mobile" => "mobile", "Location" => "location",
        "SignInName" => "sign-in name", "Vertical" => "vertical", _ => field,
    };
}
