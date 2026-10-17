using System.Security.Claims;
using System.Security.Cryptography;

namespace NabconsIntranet.Infrastructure;

public static class Roles
{
    public const string Staff = "Staff";
    public const string Publisher = "Publisher";
    public const string Approver = "Approver";
    public const string Administrator = "Administrator";
    public const string Auditor = "Auditor";
    public const string LeadViewer = "LeadViewer";

    public static readonly string[] All = { Staff, Publisher, Approver, Administrator, Auditor, LeadViewer };

    public static readonly Dictionary<string, string> Descriptions = new()
    {
        [Staff] = "Browse approved content and submit forms",
        [Publisher] = "Draft circular metadata, upload files and post notices",
        [Approver] = "Review and approve/reject assigned requests",
        [Administrator] = "Manage users, roles, master data, staff directory and backups",
        [Auditor] = "View reports and audit history (read-only)",
        [LeadViewer] = "See and manage all business leads, pipeline and dashboard (assign to COO and MD)",
    };

    public static List<string> Parse(string? roles)
    {
        var list = (roles ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(r => All.Contains(r, StringComparer.OrdinalIgnoreCase))
            .Select(r => All.First(a => a.Equals(r, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (!list.Contains(Staff)) list.Insert(0, Staff);
        return list.Distinct().ToList();
    }

    public static string Join(IEnumerable<string> roles) => string.Join(",", Parse(string.Join(",", roles)));
}

public static class Policies
{
    public const string Publisher = "Publisher";
    public const string Approver = "Approver";
    public const string Admin = "Admin";
    public const string AuditView = "AuditView";
}

public static class ClaimNames
{
    public const string UserId = "nab:uid";
    public const string DisplayName = "nab:display";
    public const string Vertical = "nab:vertical";
    public const string Stamp = "nab:stamp";
    public const string MustChangePassword = "nab:mustchange";
}

public static class UserExtensions
{
    public static long UserId(this ClaimsPrincipal u) =>
        long.TryParse(u.FindFirst(ClaimNames.UserId)?.Value, out var id) ? id : 0;

    public static string DisplayName(this ClaimsPrincipal u) =>
        u.FindFirst(ClaimNames.DisplayName)?.Value ?? u.Identity?.Name ?? "unknown";

    public static string UserName(this ClaimsPrincipal u) => u.Identity?.Name ?? "unknown";

    public static string? Vertical(this ClaimsPrincipal u) => u.FindFirst(ClaimNames.Vertical)?.Value;

    public static bool IsAdmin(this ClaimsPrincipal u) => u.IsInRole(Roles.Administrator);
    public static bool IsPublisher(this ClaimsPrincipal u) => u.IsInRole(Roles.Publisher) || u.IsAdmin();
    public static bool IsApprover(this ClaimsPrincipal u) => u.IsInRole(Roles.Approver);
    public static bool IsAuditor(this ClaimsPrincipal u) => u.IsInRole(Roles.Auditor);
    public static bool CanViewAudit(this ClaimsPrincipal u) => u.IsAuditor() || u.IsAdmin();
    /// <summary>Publishers, administrators and auditors may see every circular, including drafts and restricted ones.</summary>
    public static bool SeesAllCirculars(this ClaimsPrincipal u) => u.IsPublisher() || u.IsAuditor();

    public static IEnumerable<string> RoleNames(this ClaimsPrincipal u) => Roles.All.Where(u.IsInRole);
}

/// <summary>PBKDF2-SHA256 salted password hashing (no external library).</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2-SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" || !int.TryParse(parts[1], out var iterations)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Returns null when acceptable, otherwise the reason.</summary>
    public static string? CheckPolicy(string? password, string? username = null)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10) return "Password must be at least 10 characters long.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            return "Password must contain upper-case, lower-case and numeric characters.";
        if (!password.Any(ch => !char.IsLetterOrDigit(ch))) return "Password must contain at least one symbol (e.g. @ # $ !).";
        if (!string.IsNullOrEmpty(username) && password.Contains(username, StringComparison.OrdinalIgnoreCase))
            return "Password must not contain the user name.";
        return null;
    }

    public static string NewStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
}
