using System.Text;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

public class StaffModel(StaffRepository staff, AuditService audit) : AppPage
{
    public Paged<StaffMember> Result { get; private set; } = new();
    public string? Q { get; private set; }

    public void OnGet(string? q, int p = 1)
    {
        Q = Clean(q, 100);
        Result = staff.Search(Q, null, null, Math.Max(1, p), includeInactive: true);
    }

    public IActionResult OnGetExport()
    {
        var sb = new StringBuilder();
        sb.AppendLine(StaffImportModel.Header);
        foreach (var s in staff.All())
            sb.AppendLine(string.Join(",", new[] { s.EmployeeCode, s.FullName, s.Designation, s.Vertical, s.Location, s.OfficialEmail, s.Extension, s.Mobile,
                s.ShowMobile ? "Yes" : "No", s.ActiveFrom, s.ActiveTo }.Select(Csv.Escape)));
        audit.Log("Staff.Exported", "Staff", null, "Directory exported to CSV");
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(), "text/csv", $"staff-directory-{DateTime.Now:yyyyMMdd}.csv");
    }

    public string PageUrl(int page) => $"/Admin/Staff?q={Uri.EscapeDataString(Q ?? "")}&p={page}";
}
