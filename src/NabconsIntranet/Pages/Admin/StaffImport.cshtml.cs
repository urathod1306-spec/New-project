using System.Globalization;
using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

/// <summary>Controlled CSV import: upload → validate and preview → confirm. Upserts by EmployeeCode.</summary>
public class StaffImportModel(StaffRepository staff, AuditService audit, IntranetOptions o) : AppPage
{
    public const string Header = "EmployeeCode,FullName,Designation,Vertical,Location,OfficialEmail,Extension,Mobile,ShowMobile,ActiveFrom,ActiveTo";
    private static readonly string[] Columns = Header.Split(',');

    public sealed record Row(int Line, StaffMember Staff, bool IsNew, List<string> Errors);

    public List<Row> Rows { get; private set; } = new();
    public string? Token { get; private set; }
    public string? FileName { get; private set; }
    public int Created { get; private set; }

    private string TempFolder => Path.Combine(o.DataPath, "tmp");

    public IActionResult OnGet() => Page();

    public IActionResult OnGetTemplate()
    {
        var sample = Header + "\r\nNC2001,Example Person,Consultant,Agri & Allied,Mumbai (Head Office),example.person@nabcons.local,2201,,No,2026-04-01,\r\n";
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sample)).ToArray(), "text/csv", "staff-import-template.csv");
    }

    public async Task<IActionResult> OnPostPreviewAsync(IFormFile? file)
    {
        if (file == null || file.Length == 0) { FlashError("Choose a CSV file."); return RedirectToPage(); }
        if (!Path.GetExtension(file.FileName).Equals(".csv", StringComparison.OrdinalIgnoreCase)) { FlashError("The file must be a .csv file."); return RedirectToPage(); }
        if (file.Length > 5 * 1024 * 1024) { FlashError("CSV files are limited to 5 MB."); return RedirectToPage(); }
        string text;
        using (var r = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = await r.ReadToEndAsync();
        System.IO.Directory.CreateDirectory(TempFolder);
        foreach (var old in System.IO.Directory.EnumerateFiles(TempFolder, "import-*.csv").Where(f => System.IO.File.GetCreationTimeUtc(f) < DateTime.UtcNow.AddHours(-2)))
            try { System.IO.File.Delete(old); } catch { /* ignore */ }
        Token = Guid.NewGuid().ToString("N");
        await System.IO.File.WriteAllTextAsync(Path.Combine(TempFolder, $"import-{Token}.csv"), text);
        FileName = Path.GetFileName(file.FileName);
        Rows = Parse(text, out var headerError);
        if (headerError != null) { FlashError(headerError); return RedirectToPage(); }
        return Page();
    }

    public IActionResult OnPostConfirm(string token, string? fileName, bool deactivateMissing = false)
    {
        if (token.Length != 32 || !token.All(Uri.IsHexDigit)) return BadRequest();
        var path = Path.Combine(TempFolder, $"import-{token}.csv");
        if (!System.IO.File.Exists(path)) { FlashError("The preview has expired. Upload the file again."); return RedirectToPage(); }
        var rows = Parse(System.IO.File.ReadAllText(path), out var headerError);
        System.IO.File.Delete(path);
        if (headerError != null) { FlashError(headerError); return RedirectToPage(); }
        if (rows.Any(r => r.Errors.Count > 0)) { FlashError("The file still contains errors. Correct them and upload again."); return RedirectToPage(); }
        int created = 0, updated = 0;
        foreach (var r in rows)
        {
            if (staff.Upsert(r.Staff, User.UserName())) created++; else updated++;
        }
        var deactivated = deactivateMissing ? staff.DeactivateMissing(rows.Select(r => r.Staff.EmployeeCode).ToList(), User.UserName()) : 0;
        audit.Log("Staff.Imported", "Staff", null, $"File '{Clean(fileName, 100)}': {created} created, {updated} updated, {deactivated} deactivated (not in file)");
        Flash($"Import complete: {created} created, {updated} updated" + (deactivateMissing ? $", {deactivated} marked inactive." : "."));
        return RedirectToPage("/Admin/Staff");
    }

    private List<Row> Parse(string text, out string? headerError)
    {
        headerError = null;
        var table = Csv.Parse(text);
        if (table.Count == 0) { headerError = "The file is empty."; return new(); }
        var header = table[0].Select(h => h.Trim()).ToList();
        var index = Columns.ToDictionary(c => c, c => header.FindIndex(h => h.Equals(c, StringComparison.OrdinalIgnoreCase)));
        if (index["EmployeeCode"] < 0 || index["FullName"] < 0)
        {
            headerError = "The first row must contain column headings including EmployeeCode and FullName. Download the template.";
            return new();
        }
        if (table.Count > 5001) { headerError = "At most 5,000 rows can be imported at a time."; return new(); }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<Row>();
        for (int i = 1; i < table.Count; i++)
        {
            string? Get(string col)
            {
                var ix = index[col];
                if (ix < 0 || ix >= table[i].Count) return null;
                var v = table[i][ix].Trim();
                return v.Length == 0 ? null : v.Length > 150 ? v[..150] : v;
            }
            var errors = new List<string>();
            var code = Get("EmployeeCode") ?? "";
            if (code.Length == 0) errors.Add("EmployeeCode is missing");
            else if (!code.All(ch => char.IsLetterOrDigit(ch) || ch is '/' or '-' or '_')) errors.Add("EmployeeCode has invalid characters");
            else if (!seen.Add(code)) errors.Add("Duplicate EmployeeCode in file");
            var name = Get("FullName");
            if (name == null) errors.Add("FullName is missing");
            var email = Get("OfficialEmail");
            if (email != null && !MailAddress.TryCreate(email, out _)) errors.Add("OfficialEmail is not a valid address");
            string? Date(string col)
            {
                var v = Get(col);
                if (v == null) return null;
                foreach (var fmt in new[] { "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy" })
                    if (DateOnly.TryParseExact(v, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d.ToString("yyyy-MM-dd");
                errors.Add($"{col} '{v}' is not a date (use YYYY-MM-DD)");
                return null;
            }
            var show = (Get("ShowMobile") ?? "").ToLowerInvariant() is "yes" or "y" or "true" or "1";
            var s = new StaffMember
            {
                EmployeeCode = code, FullName = name ?? "", Designation = Get("Designation"), Vertical = Get("Vertical"), Location = Get("Location"),
                OfficialEmail = email, Extension = Get("Extension"), Mobile = Get("Mobile"), ShowMobile = show,
                ActiveFrom = Date("ActiveFrom"), ActiveTo = Date("ActiveTo"),
            };
            rows.Add(new Row(i + 1, s, code.Length > 0 && staff.GetByCode(code) == null, errors));
        }
        return rows;
    }
}
