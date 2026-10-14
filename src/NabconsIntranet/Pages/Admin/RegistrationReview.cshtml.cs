using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

public class RegistrationReviewModel(RegistrationRepository registrations, StaffRepository staff, UserRepository users, AuditService audit) : AppPage
{
    public sealed record Row(string Field, string? Directory, string? Submitted)
    {
        public bool Changed => !string.Equals((Directory ?? "").Trim(), (Submitted ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public RegistrationRequest Item { get; private set; } = new();
    public StaffMember? Staff { get; private set; }
    public List<Row> Rows { get; private set; } = new();
    public bool CodeTaken { get; private set; }
    public string SuggestedCode { get; private set; } = "";

    private bool Load(long id)
    {
        var r = registrations.Get(id);
        if (r == null) return false;
        Item = r;
        Staff = r.StaffId != null ? staff.Get(r.StaffId.Value) : null;
        Rows = new()
        {
            new("Full name", Staff?.FullName, r.FullName),
            new("Designation", Staff?.Designation, r.Designation),
            new("Vertical", Staff?.Vertical, r.Vertical),
            new("Office / location", Staff?.Location, r.Location),
            new("Official e-mail", Staff?.OfficialEmail, r.OfficialEmail),
            new("Mobile", RegistrationRepository.Normalize(Staff?.Mobile) ?? Staff?.Mobile, r.Mobile),
            new("Employee code", Staff?.EmployeeCode, r.EmployeeCode),
        };
        CodeTaken = Staff == null && !string.IsNullOrEmpty(r.EmployeeCode) && staff.GetByCode(r.EmployeeCode) != null;
        // New people must get a directory entry; offer the applicant's own code, else a provisional one the admin can change.
        SuggestedCode = !string.IsNullOrEmpty(r.EmployeeCode) && !CodeTaken ? r.EmployeeCode : $"REG-{r.Mobile}";
        return true;
    }

    public IActionResult OnGet(long id) => Load(id) ? Page() : NotFound();

    public IActionResult OnPostApprove(long id, List<string> roles, string? employeeCode)
    {
        if (!Load(id)) return NotFound();
        if (Item.Status != RegistrationStatus.Pending) { FlashError("This registration has already been decided."); return RedirectToPage(new { id }); }
        employeeCode = Clean(employeeCode, 30);
        if (Staff == null)
        {
            if (employeeCode == null) { FlashError("Enter an employee code for the staff directory entry."); return RedirectToPage(new { id }); }
            if (!employeeCode.All(ch => char.IsLetterOrDigit(ch) || ch is '/' or '-' or '_')) { FlashError("The employee code may contain only letters, digits, / - _."); return RedirectToPage(new { id }); }
            if (staff.GetByCode(employeeCode) != null) { FlashError($"Employee code {employeeCode} is already used by another directory entry."); return RedirectToPage(new { id }); }
        }
        try
        {
            var userId = registrations.Approve(Item, roles.Append(Roles.Staff), updateDirectory: true, employeeCode, User.UserName());
            var created = users.Get(userId)!;
            audit.Log("User.Created", "User", userId, $"{created.Username} ({created.DisplayName}) from self-registration #{id}; roles={created.Roles}");
            audit.Log("Registration.Approved", "Registration", id,
                $"{Item.FullName} ({Item.Mobile}); staff directory {(Staff != null ? "entry updated " + Staff.EmployeeCode : "entry added " + employeeCode)}");
            Flash($"{Item.FullName} approved. They can now sign in with mobile number {Item.Mobile}.");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostReject(long id, string? reason)
    {
        if (!Load(id)) return NotFound();
        reason = Clean(reason, 500);
        if (reason == null) { FlashError("Enter a reason for rejecting the registration."); return RedirectToPage(new { id }); }
        try
        {
            registrations.Reject(id, reason, User.UserName());
            audit.Log("Registration.Rejected", "Registration", id, $"{Item.FullName} ({Item.Mobile}): {reason}");
            Flash($"Registration of {Item.FullName} rejected.");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return RedirectToPage(new { id });
    }
}
