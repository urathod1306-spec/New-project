using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Account;

/// <summary>
/// Self-registration: (1) enter 10-digit mobile → (2) confirm/correct details (pre-filled when the number is in
/// the staff directory) and choose a password → (3) request goes to the administrator for approval.
/// </summary>
public class RegisterModel(RegistrationRepository registrations, UserRepository users, MasterRepository masters,
    AuditService audit, IntranetOptions o) : AppPage
{
    public sealed class InputModel
    {
        [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; } = "";
        [StringLength(120)] public string? Designation { get; set; }
        public string? Vertical { get; set; }
        public string? Location { get; set; }
        [StringLength(150), Display(Name = "Official e-mail")] public string? OfficialEmail { get; set; }
        [StringLength(30), Display(Name = "Employee code (if known)")] public string? EmployeeCode { get; set; }
        [Required, DataType(DataType.Password)] public string Password { get; set; } = "";
        [Required, DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = "";
    }

    [BindProperty] public string? Mobile { get; set; }
    [BindProperty] public InputModel Input { get; set; } = new();
    public int Step { get; private set; } = 1;
    public StaffMember? Match { get; private set; }
    public string? MaskedEmail { get; private set; }
    public List<string> Verticals { get; private set; } = new();
    public List<string> Locations { get; private set; } = new();

    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private void LoadLists()
    {
        Verticals = masters.Names(MasterTypes.Vertical);
        Locations = masters.Names(MasterTypes.Location);
        if (Match?.Vertical is string v && !Verticals.Contains(v)) Verticals.Insert(0, v);
        if (Match?.Location is string l && !Locations.Contains(l)) Locations.Insert(0, l);
    }

    public IActionResult OnGet()
    {
        if (!o.SelfRegistrationAvailable) return NotFound();
        if (User.Identity?.IsAuthenticated == true) return Redirect("/");
        return Page();
    }

    /// <summary>Checks the mobile number; returns an error message, or null when registration may continue.</summary>
    private string? CheckMobile()
    {
        Mobile = (Mobile ?? "").Trim();
        if (!RegistrationRepository.IsValidMobile(Mobile)) return "Enter your 10-digit mobile number (digits only, without +91 or 0).";
        if (users.GetByUsername(Mobile) != null) return "An account already exists for this mobile number. Please sign in, or contact the administrator if you have forgotten your password.";
        if (registrations.HasPending(Mobile)) return "A registration for this mobile number is already awaiting the administrator's approval.";
        return null;
    }

    public IActionResult OnPostLookup()
    {
        if (!o.SelfRegistrationAvailable) return NotFound();
        ModelState.Clear();
        if (!Throttle.Allow("reg-lookup:" + Ip, 10, TimeSpan.FromMinutes(10)))
        {
            ModelState.AddModelError("", "Too many attempts from this computer. Please wait 10 minutes and try again.");
            return Page();
        }
        var error = CheckMobile();
        if (error != null) { ModelState.AddModelError(nameof(Mobile), error); return Page(); }

        Match = registrations.FindStaffByMobile(Mobile!);
        audit.Log("Registration.Lookup", "Registration", null, $"Mobile ******{Mobile![^4..]}: {(Match != null ? "found in staff directory" : "not in directory")}", "anonymous");
        if (Match != null)
        {
            Input.FullName = Match.FullName;
            Input.Designation = Match.Designation;
            Input.Vertical = Match.Vertical;
            Input.Location = Match.Location;
            MaskedEmail = RegistrationRepository.MaskEmail(Match.OfficialEmail);
        }
        Step = 2;
        LoadLists();
        return Page();
    }

    public IActionResult OnPostSubmit()
    {
        if (!o.SelfRegistrationAvailable) return NotFound();
        Step = 2;
        var error = CheckMobile();
        if (error != null) { ModelState.Clear(); Step = 1; ModelState.AddModelError(nameof(Mobile), error); return Page(); }
        Match = registrations.FindStaffByMobile(Mobile!);   // re-evaluated on the server, never trusted from the form
        MaskedEmail = RegistrationRepository.MaskEmail(Match?.OfficialEmail);
        LoadLists();

        var email = Clean(Input.OfficialEmail, 150);
        if (email != null && !MailAddress.TryCreate(email, out _)) ModelState.AddModelError("Input.OfficialEmail", "Enter a valid e-mail address.");
        if (Match == null && email == null) ModelState.AddModelError("Input.OfficialEmail", "Enter your official e-mail address.");
        if (Input.Password != Input.ConfirmPassword) ModelState.AddModelError("Input.ConfirmPassword", "The passwords do not match.");
        if (PasswordHasher.CheckPolicy(Input.Password, Mobile) is string policy) ModelState.AddModelError("Input.Password", policy);
        if (!ModelState.IsValid) return Page();

        var id = registrations.Submit(new RegistrationRequest
        {
            Mobile = Mobile!, StaffId = Match?.StaffId,
            EmployeeCode = Match?.EmployeeCode ?? Clean(Input.EmployeeCode, 30),
            FullName = Input.FullName.Trim(), Designation = Clean(Input.Designation, 120),
            Vertical = Clean(Input.Vertical, 100), Location = Clean(Input.Location, 100),
            OfficialEmail = email ?? Match?.OfficialEmail,
            PasswordHash = PasswordHasher.Hash(Input.Password), IpAddress = Ip,
        });
        audit.Log("Registration.Submitted", "Registration", id,
            $"{Input.FullName.Trim()} (mobile ******{Mobile![^4..]}; {(Match != null ? "matched directory " + Match.EmployeeCode : "new, not in directory")})", "anonymous");
        Step = 3;
        return Page();
    }
}
