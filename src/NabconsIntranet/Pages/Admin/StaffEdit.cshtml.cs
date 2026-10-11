using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

public class StaffEditModel(StaffRepository staff, MasterRepository masters, AuditService audit) : AppPage
{
    public sealed class InputModel
    {
        [Required, StringLength(30), RegularExpression(@"^[A-Za-z0-9/_\-]+$", ErrorMessage = "Letters, digits, / _ - only."), Display(Name = "Employee code")]
        public string EmployeeCode { get; set; } = "";
        [Required, StringLength(120), Display(Name = "Full name")] public string FullName { get; set; } = "";
        [StringLength(120)] public string? Designation { get; set; }
        public string? Vertical { get; set; }
        public string? Location { get; set; }
        [EmailAddress, StringLength(150), Display(Name = "Official e-mail")] public string? OfficialEmail { get; set; }
        [StringLength(20)] public string? Extension { get; set; }
        [StringLength(20)] public string? Mobile { get; set; }
        [Display(Name = "Show mobile number in directory (disclosure approved)")] public bool ShowMobile { get; set; }
        [Display(Name = "Active from")] public DateOnly? ActiveFrom { get; set; }
        [Display(Name = "Active to (leave blank while in service)")] public DateOnly? ActiveTo { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public long Id { get; private set; }
    public List<string> Verticals { get; private set; } = new();
    public List<string> Locations { get; private set; } = new();

    private void Load()
    {
        Verticals = masters.Names(MasterTypes.Vertical);
        Locations = masters.Names(MasterTypes.Location);
    }

    public IActionResult OnGet(long? id)
    {
        Id = id ?? 0;
        Load();
        if (id != null)
        {
            var s = staff.Get(id.Value);
            if (s == null) return NotFound();
            Input = new InputModel
            {
                EmployeeCode = s.EmployeeCode, FullName = s.FullName, Designation = s.Designation, Vertical = s.Vertical, Location = s.Location,
                OfficialEmail = s.OfficialEmail, Extension = s.Extension, Mobile = s.Mobile, ShowMobile = s.ShowMobile,
                ActiveFrom = ParseDate(s.ActiveFrom), ActiveTo = ParseDate(s.ActiveTo),
            };
        }
        return Page();
    }

    public IActionResult OnPost(long? id)
    {
        Id = id ?? 0;
        Load();
        StaffMember? existing = id != null ? staff.Get(id.Value) : null;
        if (id != null && existing == null) return NotFound();
        var byCode = staff.GetByCode(Input.EmployeeCode.Trim());
        if (byCode != null && byCode.StaffId != Id) ModelState.AddModelError("Input.EmployeeCode", "Another staff entry already uses this employee code.");
        if (existing != null && !existing.EmployeeCode.Equals(Input.EmployeeCode.Trim(), StringComparison.OrdinalIgnoreCase))
            ModelState.AddModelError("Input.EmployeeCode", "The employee code is the permanent key and cannot be changed. Create a new entry instead.");
        if (!ModelState.IsValid) return Page();

        var s = new StaffMember
        {
            EmployeeCode = Input.EmployeeCode.Trim(), FullName = Input.FullName.Trim(), Designation = Clean(Input.Designation, 120),
            Vertical = Clean(Input.Vertical, 100), Location = Clean(Input.Location, 100), OfficialEmail = Clean(Input.OfficialEmail, 150),
            Extension = Clean(Input.Extension, 20), Mobile = Clean(Input.Mobile, 20), ShowMobile = Input.ShowMobile,
            ActiveFrom = IsoDate(Input.ActiveFrom), ActiveTo = IsoDate(Input.ActiveTo),
        };
        var created = staff.Upsert(s, User.UserName());
        audit.Log(created ? "Staff.Created" : "Staff.Updated", "Staff", s.StaffId, $"{s.EmployeeCode} {s.FullName}; {s.Designation}; {s.Vertical}; active to {s.ActiveTo ?? "-"}");
        Flash($"{s.FullName} saved.");
        return RedirectToPage("/Admin/Staff");
    }
}
