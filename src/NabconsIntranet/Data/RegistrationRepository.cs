using System.Text.RegularExpressions;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

public static class RegistrationStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public sealed class RegistrationRequest
{
    public long Id { get; set; }
    public string Mobile { get; set; } = "";
    public long? StaffId { get; set; }
    public string? EmployeeCode { get; set; }
    public string FullName { get; set; } = "";
    public string? Designation { get; set; }
    public string? Vertical { get; set; }
    public string? Location { get; set; }
    public string? OfficialEmail { get; set; }
    public string? PasswordHash { get; set; }
    public string Status { get; set; } = RegistrationStatus.Pending;
    public string SubmittedAt { get; set; } = "";
    public string? IpAddress { get; set; }
    public string? DecidedBy { get; set; }
    public string? DecidedAt { get; set; }
    public string? DecisionRemarks { get; set; }
    public long? CreatedUserId { get; set; }
}

/// <summary>
/// Staff self-registration: a person registers with their 10-digit mobile number (which becomes their
/// sign-in name); an administrator approves or rejects. Existing directory entries are matched by mobile.
/// </summary>
public sealed class RegistrationRepository(Db db, StaffRepository staff, UserRepository users, Fmt fmt)
{
    private static readonly Regex TenDigits = new(@"^\d{10}$", RegexOptions.Compiled);

    public static bool IsValidMobile(string? m) => m != null && TenDigits.IsMatch(m);

    /// <summary>Digits only; drops a leading +91/91/0 so "+91 98200 12345" and "098200 12345" both match "9820012345".</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var d = new string(raw.Where(char.IsDigit).ToArray());
        if (d.Length == 12 && d.StartsWith("91")) d = d[2..];
        else if (d.Length == 11 && d.StartsWith('0')) d = d[1..];
        return d.Length == 10 ? d : null;
    }

    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains('@')) return "";
        var at = email.IndexOf('@');
        var local = email[..at];
        var shown = local.Length <= 2 ? local[..1] : local[..1] + new string('*', Math.Min(local.Length - 2, 6)) + local[^1];
        return shown + email[at..];
    }

    /// <summary>Active directory entry whose mobile matches (formatting ignored).</summary>
    public StaffMember? FindStaffByMobile(string mobile)
    {
        var matches = staff.All().Where(s => Normalize(s.Mobile) == mobile).ToList();
        return matches.FirstOrDefault(s => s.IsActive(fmt.Today)) ?? matches.FirstOrDefault();
    }

    public RegistrationRequest? Get(long id) => db.Single<RegistrationRequest>("SELECT * FROM RegistrationRequests WHERE Id = @id", new { id });

    public RegistrationRequest? Latest(string mobile) =>
        db.Single<RegistrationRequest>("SELECT * FROM RegistrationRequests WHERE Mobile = @mobile ORDER BY Id DESC LIMIT 1", new { mobile });

    public bool HasPending(string mobile) =>
        db.Scalar<long>("SELECT COUNT(*) FROM RegistrationRequests WHERE Mobile = @mobile AND Status = 'Pending'", new { mobile }) > 0;

    public List<RegistrationRequest> List(string? status) => db.Query<RegistrationRequest>(
        "SELECT * FROM RegistrationRequests" + (string.IsNullOrEmpty(status) ? "" : " WHERE Status = @status") + " ORDER BY Id DESC LIMIT 500",
        new { status });

    public long PendingCount() => db.Scalar<long>("SELECT COUNT(*) FROM RegistrationRequests WHERE Status = 'Pending'");

    public long Submit(RegistrationRequest r)
    {
        r.Status = RegistrationStatus.Pending;
        r.SubmittedAt = Db.Now();
        return db.Insert("""
            INSERT INTO RegistrationRequests (Mobile, StaffId, EmployeeCode, FullName, Designation, Vertical, Location, OfficialEmail, PasswordHash, Status, SubmittedAt, IpAddress)
            VALUES (@Mobile, @StaffId, @EmployeeCode, @FullName, @Designation, @Vertical, @Location, @OfficialEmail, @PasswordHash, @Status, @SubmittedAt, @IpAddress)
            """, r);
    }

    /// <summary>Creates the sign-in account (user name = mobile) and optionally updates or adds the directory entry.</summary>
    public long Approve(RegistrationRequest r, IEnumerable<string> roles, bool updateDirectory, string? employeeCodeForNew, string actor)
    {
        if (users.GetByUsername(r.Mobile) != null) throw new WorkflowException($"A user account for {r.Mobile} already exists.");
        var userId = users.Create(new AppUser
        {
            Username = r.Mobile, DisplayName = r.FullName, Email = r.OfficialEmail, Vertical = r.Vertical,
            Roles = Infrastructure.Roles.Join(roles), PasswordHash = r.PasswordHash, MustChangePassword = false, IsActive = true,
        }, actor);

        if (updateDirectory)
        {
            var existing = r.StaffId != null ? staff.Get(r.StaffId.Value) : null;
            if (existing != null)
            {
                existing.FullName = r.FullName;
                existing.Designation = r.Designation;
                existing.Vertical = r.Vertical;
                existing.Location = r.Location;
                if (!string.IsNullOrEmpty(r.OfficialEmail)) existing.OfficialEmail = r.OfficialEmail;
                existing.ActiveTo = null; // a registered, approved person is current staff
                staff.Upsert(existing, actor);
            }
            else if (!string.IsNullOrWhiteSpace(employeeCodeForNew))
            {
                staff.Upsert(new StaffMember
                {
                    EmployeeCode = employeeCodeForNew.Trim(), FullName = r.FullName, Designation = r.Designation, Vertical = r.Vertical,
                    Location = r.Location, OfficialEmail = r.OfficialEmail, Mobile = r.Mobile, ShowMobile = false,
                    ActiveFrom = fmt.Today.ToString("yyyy-MM-dd"),
                }, actor);
            }
        }

        var n = db.Exec("""
            UPDATE RegistrationRequests SET Status = 'Approved', DecidedBy = @actor, DecidedAt = @now, CreatedUserId = @userId, PasswordHash = NULL
            WHERE Id = @Id AND Status = 'Pending'
            """, new { actor, now = Db.Now(), userId, r.Id });
        if (n == 0) throw new WorkflowException("This registration was already decided by someone else.");
        return userId;
    }

    public void Reject(long id, string reason, string actor)
    {
        var n = db.Exec("""
            UPDATE RegistrationRequests SET Status = 'Rejected', DecidedBy = @actor, DecidedAt = @now, DecisionRemarks = @reason, PasswordHash = NULL
            WHERE Id = @id AND Status = 'Pending'
            """, new { id, reason, actor, now = Db.Now() });
        if (n == 0) throw new WorkflowException("This registration was already decided by someone else.");
    }
}
