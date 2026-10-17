using System.Net.Mail;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

public sealed class ProfileChangeRequest
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long? StaffId { get; set; }
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Status { get; set; } = "Pending";
    public string RequestedAt { get; set; } = "";
    public string? DecidedBy { get; set; }
    public string? DecidedAt { get; set; }
    public string? DecisionRemarks { get; set; }
    // joined
    public string? DisplayName { get; set; }
    public string? Username { get; set; }
}

public sealed class ProfileAuditEntry
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Field { get; set; } = "";
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string ChangedBy { get; set; } = "";
    public string ChangedAt { get; set; } = "";
}

/// <summary>The editable contact fields of My Profile.</summary>
public sealed record ProfileContact(string? Mobile, string? Extension, string? OfficialEmail, string? Location);

/// <summary>
/// My Profile: links accounts to directory rows, applies a user's own contact changes (field by field, audited),
/// and handles vertical-change requests that an administrator approves.
/// </summary>
public sealed class ProfileRepository(Db db, UserRepository users, StaffRepository staff, AuditService audit, IntranetOptions o)
{
    public const string VerticalField = "Vertical";

    /// <summary>
    /// Links accounts without a directory entry, once and only when unambiguous: self-registration record,
    /// then mobile number = user name, then identical official e-mail. Never re-links or overwrites.
    /// </summary>
    public int LinkUnlinkedAccounts()
    {
        int linked = 0;
        var linkedStaff = db.Query<AppUser>("SELECT * FROM Users WHERE StaffId IS NOT NULL").Select(u => u.StaffId!.Value).ToHashSet();
        var allStaff = staff.All();
        foreach (var u in db.Query<AppUser>("SELECT * FROM Users WHERE StaffId IS NULL"))
        {
            long? sid = db.Scalar<long?>("SELECT StaffId FROM RegistrationRequests WHERE CreatedUserId = @Id AND StaffId IS NOT NULL ORDER BY Id DESC LIMIT 1", new { u.Id });
            if (sid == null)
            {
                var mobile = RegistrationRepository.IsValidMobile(u.Username) ? u.Username : null;
                var byMobile = mobile == null ? new() : allStaff.Where(s => RegistrationRepository.Normalize(s.Mobile) == mobile).ToList();
                if (byMobile.Count == 1) sid = byMobile[0].StaffId;
            }
            if (sid == null && !string.IsNullOrWhiteSpace(u.Email))
            {
                var byEmail = allStaff.Where(s => string.Equals(s.OfficialEmail?.Trim(), u.Email.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
                if (byEmail.Count == 1) sid = byEmail[0].StaffId;
            }
            if (sid == null || linkedStaff.Contains(sid.Value)) continue;
            db.Exec("UPDATE Users SET StaffId = @sid WHERE Id = @Id AND StaffId IS NULL", new { sid, u.Id });
            linkedStaff.Add(sid.Value);
            audit.Log("User.LinkedToDirectory", "User", u.Id, $"{u.Username} linked to directory entry {allStaff.First(s => s.StaffId == sid).EmployeeCode} (automatic)", "system");
            linked++;
        }
        return linked;
    }

    public void SetLink(long userId, long? staffId, string actor)
    {
        if (staffId != null && db.Scalar<long>("SELECT COUNT(*) FROM Users WHERE StaffId = @staffId AND Id <> @userId", new { staffId, userId }) > 0)
            throw new WorkflowException("That directory entry is already linked to another user account.");
        db.Exec("UPDATE Users SET StaffId = @staffId WHERE Id = @userId", new { staffId, userId });
        audit.Log("User.LinkedToDirectory", "User", userId, staffId == null ? "Directory link removed" : $"Linked to staff #{staffId}", actor);
    }

    public StaffMember? StaffFor(AppUser u) => u.StaffId != null ? staff.Get(u.StaffId.Value) : null;

    public List<ProfileAuditEntry> History(long userId) =>
        db.Query<ProfileAuditEntry>("SELECT * FROM ProfileAudit WHERE UserId = @userId ORDER BY Id DESC LIMIT 50", new { userId });

    /// <summary>Validates and normalises the contact fields. Returns field → error.</summary>
    public Dictionary<string, string> Validate(AppUser u, StaffMember s, ProfileContact c, out ProfileContact clean)
    {
        var errors = new Dictionary<string, string>();
        string? mobile = null;
        if (!string.IsNullOrWhiteSpace(c.Mobile))
        {
            mobile = RegistrationRepository.Normalize(c.Mobile);
            if (mobile == null || !"6789".Contains(mobile[0])) errors["Mobile"] = "Enter a valid 10-digit Indian mobile number.";
            else if (MobileTaken(mobile, u.Id)) errors["Mobile"] = "This mobile number is already used by another active user.";
        }
        var ext = string.IsNullOrWhiteSpace(c.Extension) ? null : c.Extension.Trim();
        if (ext != null && (ext.Length > 20 || !ext.All(ch => char.IsDigit(ch) || ch is ' ' or '-' or '+' or '(' or ')' or '/')))
            errors["Extension"] = "Use digits only (spaces, - + ( ) / allowed), at most 20 characters.";
        var email = string.IsNullOrWhiteSpace(c.OfficialEmail) ? null : c.OfficialEmail.Trim();
        if (email != null)
        {
            if (email.Length > 150 || !MailAddress.TryCreate(email, out var addr) || addr.Address != email) errors["OfficialEmail"] = "Enter a valid e-mail address.";
            else if (!string.IsNullOrWhiteSpace(o.OfficialEmailDomain) && !email.EndsWith("@" + o.OfficialEmailDomain.Trim().TrimStart('@'), StringComparison.OrdinalIgnoreCase))
                errors["OfficialEmail"] = $"Use your official @{o.OfficialEmailDomain.Trim().TrimStart('@')} address.";
        }
        var loc = string.IsNullOrWhiteSpace(c.Location) ? null : c.Location.Trim();
        if (loc != null && loc.Length > 100) errors["Location"] = "At most 100 characters.";
        clean = new ProfileContact(mobile, ext, email, loc);
        return errors;
    }

    private bool MobileTaken(string mobile, long userId)
    {
        var otherActiveStaff = db.Query<StaffMember>("""
            SELECT s.* FROM Staff s JOIN Users u ON u.StaffId = s.StaffId
            WHERE u.IsActive = 1 AND u.Id <> @userId AND s.Mobile IS NOT NULL
            """, new { userId });
        if (otherActiveStaff.Any(s => RegistrationRepository.Normalize(s.Mobile) == mobile)) return true;
        return db.Scalar<long>("SELECT COUNT(*) FROM Users WHERE Username = @mobile AND Id <> @userId", new { mobile, userId }) > 0;
    }

    /// <summary>
    /// Applies the user's own contact changes to their directory entry. If they sign in with their mobile
    /// number, their sign-in name follows the new number. Returns the list of fields changed.
    /// </summary>
    public List<string> ApplyContact(AppUser u, StaffMember s, ProfileContact c)
    {
        var changes = new List<(string field, string? oldV, string? newV)>();
        void Track(string f, string? oldV, string? newV) { if (!string.Equals(oldV ?? "", newV ?? "", StringComparison.Ordinal)) changes.Add((f, oldV, newV)); }
        Track("Mobile", s.Mobile, c.Mobile);
        Track("Extension", s.Extension, c.Extension);
        Track("OfficialEmail", s.OfficialEmail, c.OfficialEmail);
        Track("Location", s.Location, c.Location);
        if (changes.Count == 0) return new();

        var now = Db.Now();
        var renameLogin = changes.Any(x => x.field == "Mobile") && RegistrationRepository.IsValidMobile(u.Username) && c.Mobile != null && c.Mobile != u.Username;
        using var con = db.Open();
        using var tx = con.BeginTransaction();
        Db.Exec(con, tx, """
            UPDATE Staff SET Mobile = @Mobile, Extension = @Extension, OfficialEmail = @OfficialEmail, Location = @Location,
                   UpdatedBy = @by, UpdatedAt = @now WHERE StaffId = @StaffId
            """, new { c.Mobile, c.Extension, c.OfficialEmail, c.Location, by = u.Username, now, s.StaffId });
        if (changes.Any(x => x.field == "OfficialEmail"))
            Db.Exec(con, tx, "UPDATE Users SET Email = @e, UpdatedBy = @by, UpdatedAt = @now WHERE Id = @id", new { e = c.OfficialEmail, by = u.Username, now, id = u.Id });
        if (renameLogin)
        {
            Db.Exec(con, tx, "UPDATE Users SET Username = @n, UpdatedBy = @by, UpdatedAt = @now WHERE Id = @id", new { n = c.Mobile, by = u.Username, now, id = u.Id });
            changes.Add(("SignInName", u.Username, c.Mobile));
        }
        foreach (var (field, oldV, newV) in changes)
            Db.Insert(con, tx, """
                INSERT INTO ProfileAudit (UserId, StaffId, Field, OldValue, NewValue, ChangedBy, ChangedAt)
                VALUES (@uid, @sid, @field, @oldV, @newV, @by, @now)
                """, new { uid = u.Id, sid = s.StaffId, field, oldV, newV, by = u.Username, now });
        tx.Commit();
        foreach (var (field, oldV, newV) in changes)
            audit.Log("Profile.Updated", "User", u.Id, $"{field}: '{oldV}' -> '{newV}' (staff {s.EmployeeCode})", u.Username);
        return changes.Select(x => x.field).ToList();
    }

    public ProfileChangeRequest? PendingVertical(long userId) => db.Single<ProfileChangeRequest>(
        "SELECT * FROM ProfileChangeRequests WHERE UserId = @userId AND Field = 'Vertical' AND Status = 'Pending' ORDER BY Id DESC LIMIT 1", new { userId });

    /// <summary>Requests (or, when approval is switched off, applies) a vertical change.</summary>
    public bool RequestVertical(AppUser u, StaffMember? s, string? newVertical)
    {
        newVertical = string.IsNullOrWhiteSpace(newVertical) ? null : newVertical.Trim();
        var current = u.Vertical;
        if (string.Equals(current ?? "", newVertical ?? "", StringComparison.OrdinalIgnoreCase)) return false;
        if (PendingVertical(u.Id) != null) throw new WorkflowException("A vertical change is already waiting for approval.");
        if (!o.VerticalChangeRequiresApproval)
        {
            ApplyVertical(u.Id, s?.StaffId, current, newVertical, u.Username);
            return true;
        }
        var id = db.Insert("""
            INSERT INTO ProfileChangeRequests (UserId, StaffId, Field, OldValue, NewValue, Status, RequestedAt)
            VALUES (@uid, @sid, 'Vertical', @current, @newVertical, 'Pending', @now)
            """, new { uid = u.Id, sid = s?.StaffId, current, newVertical, now = Db.Now() });
        audit.Log("Profile.VerticalChangeRequested", "ProfileChange", id, $"{u.Username}: '{current}' -> '{newVertical}'", u.Username);
        return true;
    }

    private void ApplyVertical(long userId, long? staffId, string? oldV, string? newV, string actor)
    {
        var now = Db.Now();
        using var con = db.Open();
        using var tx = con.BeginTransaction();
        Db.Exec(con, tx, "UPDATE Users SET Vertical = @newV, UpdatedBy = @actor, UpdatedAt = @now WHERE Id = @userId", new { newV, actor, now, userId });
        if (staffId != null) Db.Exec(con, tx, "UPDATE Staff SET Vertical = @newV, UpdatedBy = @actor, UpdatedAt = @now WHERE StaffId = @staffId", new { newV, actor, now, staffId });
        Db.Insert(con, tx, "INSERT INTO ProfileAudit (UserId, StaffId, Field, OldValue, NewValue, ChangedBy, ChangedAt) VALUES (@userId, @staffId, 'Vertical', @oldV, @newV, @actor, @now)",
            new { userId, staffId, oldV, newV, actor, now });
        tx.Commit();
        audit.Log("Profile.VerticalChanged", "User", userId, $"'{oldV}' -> '{newV}'", actor);
    }

    public List<ProfileChangeRequest> Requests(string? status) => db.Query<ProfileChangeRequest>(
        "SELECT r.*, u.DisplayName, u.Username FROM ProfileChangeRequests r JOIN Users u ON u.Id = r.UserId" +
        (string.IsNullOrEmpty(status) ? "" : " WHERE r.Status = @status") + " ORDER BY r.Id DESC LIMIT 500", new { status });

    public long PendingCount() => db.Scalar<long>("SELECT COUNT(*) FROM ProfileChangeRequests WHERE Status = 'Pending'");

    public void Decide(long id, bool approve, string? remarks, string actor)
    {
        var r = db.Single<ProfileChangeRequest>("SELECT * FROM ProfileChangeRequests WHERE Id = @id", new { id })
            ?? throw new WorkflowException("Request not found.");
        if (r.Status != "Pending") throw new WorkflowException("This request has already been decided.");
        if (!approve && string.IsNullOrWhiteSpace(remarks)) throw new WorkflowException("Enter a reason for rejecting the change.");
        var n = db.Exec("UPDATE ProfileChangeRequests SET Status = @st, DecidedBy = @actor, DecidedAt = @now, DecisionRemarks = @remarks WHERE Id = @id AND Status = 'Pending'",
            new { st = approve ? "Approved" : "Rejected", actor, now = Db.Now(), remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks.Trim(), id });
        if (n == 0) throw new WorkflowException("This request has already been decided.");
        if (approve)
        {
            var u = users.Get(r.UserId);
            ApplyVertical(r.UserId, u?.StaffId ?? r.StaffId, u?.Vertical, r.NewValue, actor);
        }
        audit.Log(approve ? "Profile.VerticalChangeApproved" : "Profile.VerticalChangeRejected", "ProfileChange", id, $"'{r.OldValue}' -> '{r.NewValue}'" + (approve ? "" : "; " + remarks), actor);
    }
}
