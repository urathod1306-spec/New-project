namespace NabconsIntranet.Data;

public sealed class AppUser
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Email { get; set; }
    public string? Vertical { get; set; }
    public string Roles { get; set; } = "Staff";
    public string? PasswordHash { get; set; }
    public bool MustChangePassword { get; set; }
    public long FailedLogins { get; set; }
    public string? LockoutUntil { get; set; }
    public bool IsActive { get; set; } = true;
    public long? StaffId { get; set; }
    public long PhotoVersion { get; set; }
    /// <summary>Preference: go to My Desk after signing in (default on).</summary>
    public bool OpenDeskAfterSignIn { get; set; } = true;
    public string SecurityStamp { get; set; } = "";
    public string? LastLoginAt { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? UpdatedBy { get; set; }
    public string? UpdatedAt { get; set; }

    public List<string> RoleList => Infrastructure.Roles.Parse(Roles);
    public bool IsLockedOut => LockoutUntil != null && DateTime.TryParse(LockoutUntil, null, System.Globalization.DateTimeStyles.RoundtripKind, out var u) && u > DateTime.UtcNow;
}

public sealed class MasterItem
{
    public long Id { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public long SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Verticals: short code used in e-Note numbers (NCS/EN/&lt;code&gt;/…); empty = GEN.</summary>
    public string? Code { get; set; }
}

public static class MasterTypes
{
    public const string Vertical = "Vertical";
    public const string Location = "Location";
    public const string CircularCategory = "CircularCategory";
    public const string RequestCategory = "RequestCategory";
    public const string ClientType = "ClientType";
    public const string LeadSector = "LeadSector";
    public const string LeadSource = "LeadSource";
    public const string LeadLossReason = "LeadLossReason";
    public const string ENoteCategory = "ENoteCategory";

    public static readonly Dictionary<string, string> Labels = new()
    {
        [Vertical] = "Verticals / Departments",
        [Location] = "Offices / Locations",
        [CircularCategory] = "Circular categories",
        [RequestCategory] = "Request categories",
        [ClientType] = "Lead client types",
        [LeadSector] = "Lead sectors / domains",
        [LeadSource] = "Lead sources",
        [LeadLossReason] = "Lead lost / dropped reasons",
        [ENoteCategory] = "e-Note categories",
    };
}

public sealed class StaffMember
{
    public long StaffId { get; set; }
    public string EmployeeCode { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Designation { get; set; }
    public string? Vertical { get; set; }
    public string? Location { get; set; }
    public string? OfficialEmail { get; set; }
    public string? Extension { get; set; }
    public string? Mobile { get; set; }
    public bool ShowMobile { get; set; }
    public string? PhotographPath { get; set; }
    public string? ActiveFrom { get; set; }
    public string? ActiveTo { get; set; }
    public string? UpdatedBy { get; set; }
    public string? UpdatedAt { get; set; }

    // joined: the linked user account's photo (if any)
    public long? PhotoUserId { get; set; }
    public long PhotoVersion { get; set; }

    public bool IsActive(DateOnly today) =>
        string.IsNullOrEmpty(ActiveTo) || string.CompareOrdinal(ActiveTo, today.ToString("yyyy-MM-dd")) >= 0;
}

public static class CircularStatus
{
    public const string Draft = "Draft";
    public const string Published = "Published";
    public const string Withdrawn = "Withdrawn";
}

public static class Visibility
{
    public const string AllStaff = "AllStaff";
    public const string Vertical = "Vertical";
    public const string Restricted = "Restricted";

    public static readonly Dictionary<string, string> Labels = new()
    {
        [AllStaff] = "All staff",
        [Vertical] = "Issuing vertical only",
        [Restricted] = "Restricted (publishers, administrators, auditors)",
    };
}

public sealed class Circular
{
    public long Id { get; set; }
    public string CircularNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string? IssuingVertical { get; set; }
    public string IssueDate { get; set; } = "";
    public string? EffectiveDate { get; set; }
    public string? Keywords { get; set; }
    public string? Synopsis { get; set; }
    public string Visibility { get; set; } = Data.Visibility.AllStaff;
    public string Status { get; set; } = CircularStatus.Draft;
    public long? SupersedesId { get; set; }
    public long? SupersededById { get; set; }
    public long CurrentVersion { get; set; }
    public string CreatedBy { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string? UpdatedBy { get; set; }
    public string? UpdatedAt { get; set; }
    public string? PublishedBy { get; set; }
    public string? PublishedAt { get; set; }
    public string? WithdrawnBy { get; set; }
    public string? WithdrawnAt { get; set; }
    public string? WithdrawalReason { get; set; }

    // joined
    public string? SupersededByNumber { get; set; }
    public string? SupersedesNumber { get; set; }

    public string DisplayStatus => Status == CircularStatus.Published && SupersededById != null ? "Superseded" : Status;
}

public sealed class CircularFile
{
    public long Id { get; set; }
    public long CircularId { get; set; }
    public long Version { get; set; }
    public string StoredPath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public string UploadedBy { get; set; } = "";
    public string UploadedAt { get; set; } = "";
}

public sealed class Notice
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public bool IsImportant { get; set; }
    public string StartsOn { get; set; } = "";
    public string? EndsOn { get; set; }
    public bool IsActive { get; set; } = true;
    public string PostedBy { get; set; } = "";
    public string PostedAt { get; set; } = "";
}

public static class RequestStates
{
    public const string Draft = "Draft";
    public const string Submitted = "Submitted";
    public const string UnderReview = "Under Review";
    public const string Approved = "Approved";
    public const string Returned = "Returned";
    public const string Rejected = "Rejected";
    public const string Closed = "Closed";

    public static readonly string[] All = { Draft, Submitted, UnderReview, Approved, Returned, Rejected, Closed };
    public static readonly string[] Pending = { Submitted, UnderReview };
}

public sealed class ApprovalRequest
{
    public long Id { get; set; }
    public string? RequestNumber { get; set; }
    public string FormType { get; set; } = "GeneralAdministrativeApproval";
    public string Subject { get; set; } = "";
    public string? RequestCategory { get; set; }
    public double? Amount { get; set; }
    public string? RequiredBy { get; set; }
    public string Details { get; set; } = "";
    public long SubmitterUserId { get; set; }
    public string SubmitterName { get; set; } = "";
    public string? SubmitterVertical { get; set; }
    public long? ReviewerUserId { get; set; }
    public long? ApproverUserId { get; set; }
    public string State { get; set; } = RequestStates.Draft;
    public long? AssignedToUserId { get; set; }
    public long RowVersion { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? SubmittedAt { get; set; }
    public string UpdatedAt { get; set; } = "";
    public string? ClosedAt { get; set; }

    // joined
    public string? ReviewerName { get; set; }
    public string? ApproverName { get; set; }
    public string? AssignedToName { get; set; }
}

public sealed class ApprovalAction
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public string Action { get; set; } = "";
    public string? FromState { get; set; }
    public string ToState { get; set; } = "";
    public long ActorUserId { get; set; }
    public string ActorName { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public string? Remarks { get; set; }
    public string Timestamp { get; set; } = "";
}

public sealed class Attachment
{
    public long Id { get; set; }
    public long RequestId { get; set; }
    public string StoredPath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public long UploadedByUserId { get; set; }
    public string UploadedBy { get; set; } = "";
    public string UploadedAt { get; set; } = "";
}

public sealed class Paged<T>
{
    public List<T> Items { get; init; } = new();
    public long Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int Pages => PageSize == 0 ? 1 : (int)Math.Max(1, Math.Ceiling(Total / (double)PageSize));
}
