using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

// =====================================================================================================================
// Compliance Management (release 1.6.0) — design: docs/designs/DD-07-compliance-and-inward.md
// This file: fixed codes, settings, records and the access rules. Queries: ComplianceRepository.cs;
// every state change: ComplianceWorkflow.cs; CSV import: ComplianceImport.cs; printed ATRs: AtrBuilder.cs.
// =====================================================================================================================

/// <summary>Kinds of event (DD-07 §4.1). The code decides the default ATR template and whether the event is restricted.</summary>
public static class ComplianceSource
{
    public const string Board = "Board", AuditCommittee = "AuditCommittee", BoardCommittee = "BoardCommittee",
        NabardInspection = "NabardInspection", StatutoryAudit = "StatutoryAudit", InternalAudit = "InternalAudit",
        ReviewMeeting = "ReviewMeeting", Other = "Other";

    public static readonly (string Code, string Label, string Template)[] All =
    {
        (Board, "Board of Directors", AtrTemplate.BoardAtr),
        (AuditCommittee, "Audit Committee of the Board", AtrTemplate.BoardAtr),
        (BoardCommittee, "Other Board sub-committee (BD, HR, Risk, …)", AtrTemplate.BoardAtr),
        (NabardInspection, "NABARD inspection", AtrTemplate.ObservationReply),
        (StatutoryAudit, "Statutory / C&AG audit", AtrTemplate.ObservationReply),
        (InternalAudit, "Internal audit", AtrTemplate.InternalAudit),
        (ReviewMeeting, "Review meeting (subsidiaries, strategic, NABARD oversight)", AtrTemplate.BoardAtr),
        (Other, "Other", AtrTemplate.ObservationReply),
    };

    public static bool IsValid(string? c) => All.Any(a => a.Code == c);
    public static string Label(string? c) => All.FirstOrDefault(a => a.Code == c).Label ?? c ?? "";
    public static string Template(string? c) => All.FirstOrDefault(a => a.Code == c).Template ?? AtrTemplate.ObservationReply;
}

public static class AtrTemplate
{
    public const string BoardAtr = "BoardAtr", ObservationReply = "ObservationReply", InternalAudit = "InternalAudit";
    public static readonly Dictionary<string, string> Labels = new()
    {
        [BoardAtr] = "Board / committee ATR (multi-meeting pack)",
        [ObservationReply] = "Observation – reply (statutory, C&AG, NABARD inspection)",
        [InternalAudit] = "Internal audit compliance sheet",
    };
}

/// <summary>Action-point status (DD-07 §3.3). Open = Ongoing, PendingExternal, ProposedClosure.</summary>
public static class CStatus
{
    public const string Ongoing = "Ongoing", PendingExternal = "PendingExternal", ProposedClosure = "ProposedClosure",
        Completed = "Completed", Dropped = "Dropped", Merged = "Merged";
    public static readonly string[] Open = { Ongoing, PendingExternal, ProposedClosure };
    public static readonly string[] All = { Ongoing, PendingExternal, ProposedClosure, Completed, Dropped, Merged };
    public const string OpenSql = "('Ongoing','PendingExternal','ProposedClosure')";
    public static bool IsOpen(string s) => Open.Contains(s);
    public static string Label(string s) => s switch
    {
        PendingExternal => "Pending with external party (guidance awaited)",
        ProposedClosure => "Proposed for closure",
        _ => s,
    };
}

public static class ComplianceCycle
{
    public const string NextMeeting = "NextMeeting", Quarterly = "Quarterly", Monthly = "Monthly", OneTime = "OneTime";
    public static readonly Dictionary<string, string> Labels = new()
    {
        [NextMeeting] = "Every meeting / submission", [Quarterly] = "Quarterly", [Monthly] = "Monthly", [OneTime] = "One-time",
    };
}

public static class CompliancePriority
{
    public static readonly string[] All = { "High", "Medium", "Low" };
}

/// <summary>Compliance access grants, held in ComplianceMembers (not in Users.Roles; see DD-07 D12).</summary>
public static class ComplianceGrant
{
    public const string Admin = "ComplianceAdmin", Viewer = "ComplianceViewer";
    public static readonly Dictionary<string, string> Labels = new()
    {
        [Admin] = "Compliance Admin (Compliance Cell)",
        [Viewer] = "Compliance Viewer (read and print everything)",
    };
}

/// <summary>Settings from the "Compliance" section of appsettings.json; every key is optional (DD-07 §9).</summary>
public sealed class ComplianceOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>Null when absent from the file, so a list given in the file replaces the default instead of being added to it.</summary>
    public List<string>? RestrictedSourceTypes { get; set; }
    public bool AuditorSeesRestricted { get; set; }
    public int DueSoonDays { get; set; } = 7;
    public int OverdueRepeatDays { get; set; } = 7;
    public int UpdateLeadDays { get; set; } = 15;
    public int ReminderHour { get; set; } = 9;
    public string SignatureBlock { get; set; } = "Company Secretary";
    public int MaxImportRows { get; set; } = 500;
    public int MaxImportKB { get; set; } = 1024;
    public string DefaultOfficerCodeFormat { get; set; } = "Code";

    /// <summary>Validated restricted source-type codes (case-insensitive match against the fixed codes).</summary>
    public IReadOnlyList<string> Restricted { get; private set; } = new[] { ComplianceSource.Board, ComplianceSource.AuditCommittee };
    public List<string> UnknownRestricted { get; } = new();

    public void Resolve()
    {
        if (RestrictedSourceTypes != null)
        {
            var list = new List<string>();
            foreach (var raw in RestrictedSourceTypes.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0))
            {
                if (raw.Equals("None", StringComparison.OrdinalIgnoreCase)) continue;
                var code = ComplianceSource.All.FirstOrDefault(a => a.Code.Equals(raw, StringComparison.OrdinalIgnoreCase)).Code;
                if (code != null) list.Add(code); else UnknownRestricted.Add(raw);
            }
            Restricted = list.Distinct().ToList();
        }
        DueSoonDays = Math.Clamp(DueSoonDays, 1, 90);
        OverdueRepeatDays = Math.Clamp(OverdueRepeatDays, 1, 90);
        UpdateLeadDays = Math.Clamp(UpdateLeadDays, 0, 90);
        ReminderHour = Math.Clamp(ReminderHour, 0, 23);
        MaxImportRows = Math.Clamp(MaxImportRows, 1, 5000);
        MaxImportKB = Math.Clamp(MaxImportKB, 16, 1900);       // the import page accepts at most 2 MB bodies
        if (DefaultOfficerCodeFormat is not ("Code" or "Name" or "NameAndCode")) DefaultOfficerCodeFormat = "Code";
        SignatureBlock = (SignatureBlock ?? "").Replace("\\n", "\n");
    }
}

// ------------------------------------------------------------------------------------------------- records

public sealed class ComplianceEvent
{
    public long Id { get; set; }
    public string SourceType { get; set; } = "";
    public long? ForumId { get; set; }
    public string Forum { get; set; } = "";
    public string? EventNo { get; set; }
    public string Title { get; set; } = "";
    public string? HeldOn { get; set; }
    public string? PeriodFrom { get; set; }
    public string? PeriodTo { get; set; }
    public string? ReportReceivedOn { get; set; }
    public string? ReportedTo { get; set; }
    public string ReportingCycle { get; set; } = ComplianceCycle.NextMeeting;
    public string? NextReportingOn { get; set; }
    public bool IsConfidential { get; set; }
    public string? SourceKeptAt { get; set; }
    public string? Remarks { get; set; }
    public long CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public long RowVersion { get; set; }
    // list columns
    public long OpenCount { get; set; }
    public long TotalCount { get; set; }
    /// <summary>The date the event is known by: held on, else period end, else report received.</summary>
    public string? DateKey => HeldOn ?? PeriodTo ?? ReportReceivedOn;
    public string Heading(Fmt f) => $"{(string.IsNullOrEmpty(EventNo) ? "" : EventNo + " — ")}{Title}";
}

public sealed class CompliancePoint
{
    public long Id { get; set; }
    public long EventId { get; set; }
    public string Number { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public long Seq { get; set; }
    public string? RefNo { get; set; }
    public string? RefSubject { get; set; }
    public string PointText { get; set; } = "";
    public string? Category { get; set; }
    public string Priority { get; set; } = "Medium";
    public string? OwnerVertical { get; set; }
    public long PrimaryOfficerUserId { get; set; }
    public string? OfficerCode { get; set; }
    public string RaisedOn { get; set; } = "";
    public string OriginalDueDate { get; set; } = "";
    public string CurrentDueDate { get; set; } = "";
    public string? NextUpdateDue { get; set; }
    public double? Amount { get; set; }
    public long? CarriedFromId { get; set; }
    public string Status { get; set; } = CStatus.Ongoing;
    public string? AtrText { get; set; }
    public string? AtrAsOn { get; set; }
    public string? PapersKeptAt { get; set; }
    public long? PendingUpdateId { get; set; }
    public long? PendingRevisionId { get; set; }
    public string? LastReturnedAt { get; set; }
    public string? ClosedAt { get; set; }
    public string? ClosedByName { get; set; }
    public string? ClosureRemarks { get; set; }
    public string? ClosureAuthority { get; set; }
    public long? MergedIntoId { get; set; }
    public string? Reminded7For { get; set; }
    public string? Reminded1For { get; set; }
    public string? LastOverdueReminderOn { get; set; }
    public string? UpdateRemindedFor { get; set; }
    public string CreatedByName { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    public long RowVersion { get; set; }
    // joined from the event and the officer
    public string SourceType { get; set; } = "";
    public long? ForumId { get; set; }
    public string Forum { get; set; } = "";
    public string? EventNo { get; set; }
    public string EventTitle { get; set; } = "";
    public string? HeldOn { get; set; }
    public bool IsConfidential { get; set; }
    public string ReportingCycle { get; set; } = "";
    public string? NextReportingOn { get; set; }
    public string OfficerName { get; set; } = "";
    public bool OfficerActive { get; set; } = true;

    public bool IsOpen => CStatus.IsOpen(Status);
    public bool IsOverdue(DateOnly today) => IsOpen && string.CompareOrdinal(CurrentDueDate, today.ToString("yyyy-MM-dd")) < 0;
}

public sealed class ComplianceUpdate
{
    public long Id { get; set; }
    public long PointId { get; set; }
    public long SubmittedByUserId { get; set; }
    public string SubmittedByName { get; set; } = "";
    public string SubmittedAt { get; set; } = "";
    public string AsOn { get; set; } = "";
    public string UpdateText { get; set; } = "";
    public string ProposedStatus { get; set; } = "";
    public string? PapersKeptAt { get; set; }
    public string ReviewState { get; set; } = "Pending";
    public long? ReviewedByUserId { get; set; }
    public string? ReviewedByName { get; set; }
    public string? ReviewedAt { get; set; }
    public string? ReviewRemarks { get; set; }
    public string? AtrText { get; set; }
}

public sealed class ComplianceRevision
{
    public long Id { get; set; }
    public long PointId { get; set; }
    public long RequestedByUserId { get; set; }
    public string RequestedByName { get; set; } = "";
    public string RequestedAt { get; set; } = "";
    public string OldDate { get; set; } = "";
    public string NewDate { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public long? DecidedByUserId { get; set; }
    public string? DecidedByName { get; set; }
    public string? DecidedAt { get; set; }
    public string? DecisionRemarks { get; set; }
}

public sealed class ComplianceHistoryEntry
{
    public long Id { get; set; }
    public long? PointId { get; set; }
    public long? EventId { get; set; }
    public string At { get; set; } = "";
    public long? ActorUserId { get; set; }
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Detail { get; set; }
}

public sealed class ComplianceMember
{
    public long UserId { get; set; }
    public string Role { get; set; } = "";
    public string GrantedByName { get; set; } = "";
    public string GrantedAt { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Username { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class ComplianceMemberChange
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Action { get; set; } = "";
    public string? Role { get; set; }
    public string ActorName { get; set; } = "";
    public string At { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class ComplianceSnapshot
{
    public long Id { get; set; }
    public string Template { get; set; } = "";
    public long? ForumId { get; set; }
    public string Forum { get; set; } = "";
    public string Title { get; set; } = "";
    public string SubmittedTo { get; set; } = "";
    public string SubmittedOn { get; set; } = "";
    public string AsOn { get; set; } = "";
    public string OptionsJson { get; set; } = "";
    public string HeaderJson { get; set; } = "";
    public long ItemCount { get; set; }
    public string Sha256 { get; set; } = "";
    public long CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";
    public string CreatedAt { get; set; } = "";
    public string? VoidReason { get; set; }
    public string? VoidedByName { get; set; }
    public string? VoidedAt { get; set; }
    public bool IsVoided => VoidedAt != null;
}

public sealed class ComplianceSnapshotItem
{
    public long SnapshotId { get; set; }
    public long Seq { get; set; }
    public long Section { get; set; }
    public long? PointId { get; set; }
    public string Cells { get; set; } = "[]";
    public bool Bold { get; set; }
    public string? ReportedText { get; set; }
    public string? Status { get; set; }
}

public sealed class PersonOption
{
    public long Id { get; set; }
    public string DisplayName { get; set; } = "";
    public string Username { get; set; } = "";
}

// ------------------------------------------------------------------------------------------------- access rules

/// <summary>
/// Who may see and do what in the compliance module (DD-07 §5). Grants are cached in memory (single server process)
/// and reloaded after every change. Every list, count and export uses the same SQL visibility predicate.
/// </summary>
public sealed class ComplianceAccess(Db db, ComplianceOptions o)
{
    // Grants are cached for at most 30 seconds, so changes made outside this process (the --grant-compliance-admin
    // command) or by deactivating/reactivating a user take effect without a restart; in-process changes call Invalidate().
    private volatile Dictionary<long, string>? _grants;
    private long _loadedTicks;
    private static readonly long Ttl = TimeSpan.FromSeconds(30).Ticks;

    public ComplianceOptions Options => o;

    private Dictionary<long, string> Grants
    {
        get
        {
            var g = _grants;
            if (g != null && DateTime.UtcNow.Ticks - Interlocked.Read(ref _loadedTicks) < Ttl) return g;
            g = db.Query<ComplianceMember>("SELECT m.UserId, m.Role FROM ComplianceMembers m JOIN Users u ON u.Id = m.UserId WHERE u.IsActive = 1")
                .ToDictionary(m => m.UserId, m => m.Role);
            Interlocked.Exchange(ref _loadedTicks, DateTime.UtcNow.Ticks);
            _grants = g;
            return g;
        }
    }

    public void Invalidate() => _grants = null;

    public string? GrantOf(long userId) => Grants.TryGetValue(userId, out var g) ? g : null;
    public bool IsAdmin(ClaimsPrincipal u) => o.Enabled && GrantOf(u.UserId()) == ComplianceGrant.Admin;
    public bool IsViewer(ClaimsPrincipal u) => o.Enabled && GrantOf(u.UserId()) == ComplianceGrant.Viewer;
    public bool IsAuditor(ClaimsPrincipal u) => o.Enabled && u.IsAuditor();
    /// <summary>Sees every non-restricted event (CA, CV, Auditor).</summary>
    public bool SeesAll(ClaimsPrincipal u) => IsAdmin(u) || IsViewer(u) || IsAuditor(u);
    /// <summary>Also sees restricted events.</summary>
    public bool SeesRestricted(ClaimsPrincipal u) => IsAdmin(u) || IsViewer(u) || (IsAuditor(u) && o.AuditorSeesRestricted);
    public IReadOnlyList<long> ActiveAdminIds() => Grants.Where(g => g.Value == ComplianceGrant.Admin).Select(g => g.Key).ToList();

    public bool IsRestricted(string sourceType, bool confidential) => confidential || o.Restricted.Contains(sourceType);

    /// <summary>SQL: is the event aliased <paramref name="e"/> restricted? Codes come from the fixed list only.</summary>
    public string RestrictedSql(string e) => o.Restricted.Count == 0 ? $"({e}.IsConfidential = 1)"
        : $"({e}.IsConfidential = 1 OR {e}.SourceType IN ({string.Join(",", o.Restricted.Select(c => "'" + c + "'"))}))";

    /// <summary>SQL: the user (@uid) is the primary or a supporting officer of the point aliased <paramref name="p"/>.</summary>
    public static string OfficerSql(string p) =>
        $"({p}.PrimaryOfficerUserId = @uid OR EXISTS (SELECT 1 FROM ComplianceSupportOfficers so WHERE so.PointId = {p}.Id AND so.UserId = @uid))";

    /// <summary>SQL predicate for points (alias p, its event alias e) the user may see. Needs parameter @uid.</summary>
    public string PointVisibleSql(ClaimsPrincipal u, string p = "p", string e = "e")
    {
        if (SeesRestricted(u)) return "1 = 1";
        if (SeesAll(u)) return $"(NOT {RestrictedSql(e)} OR {OfficerSql(p)})";
        return OfficerSql(p);
    }

    /// <summary>SQL predicate for points the user may see in the management views (dashboard, lists, exports, ATRs): officers' own views use <see cref="OfficerSql"/>.</summary>
    public string ManagedVisibleSql(ClaimsPrincipal u, string e = "e") =>
        SeesRestricted(u) ? "1 = 1" : SeesAll(u) ? $"NOT {RestrictedSql(e)}" : "0 = 1";

    /// <summary>SQL predicate for events (alias e) the user may see (header).</summary>
    public string EventVisibleSql(ClaimsPrincipal u, string e = "e")
    {
        var officer = $"EXISTS (SELECT 1 FROM CompliancePoints xp WHERE xp.EventId = {e}.Id AND {OfficerSql("xp")})";
        if (SeesRestricted(u)) return "1 = 1";
        if (SeesAll(u)) return $"(NOT {RestrictedSql(e)} OR {officer})";
        return officer;
    }

    public bool CanSeeEventManaged(ComplianceEvent ev, ClaimsPrincipal u) =>
        SeesRestricted(u) || (SeesAll(u) && !IsRestricted(ev.SourceType, ev.IsConfidential));

    public bool IsOfficer(long pointId, long userId) => db.Scalar<long>(
        "SELECT COUNT(*) FROM CompliancePoints p WHERE p.Id = @pointId AND " + OfficerSql("p").Replace("@uid", "@userId"),
        new { pointId, userId }) > 0;

    public static bool IsOfficer(SqliteConnection c, SqliteTransaction tx, long pointId, long userId) => Db.Scalar<long>(c, tx,
        "SELECT COUNT(*) FROM CompliancePoints p WHERE p.Id = @pointId AND " + OfficerSql("p").Replace("@uid", "@userId"),
        new { pointId, userId }) > 0;

    public bool CanViewPoint(CompliancePoint p, ClaimsPrincipal u) =>
        o.Enabled && (SeesRestricted(u) || (SeesAll(u) && !IsRestricted(p.SourceType, p.IsConfidential)) || IsOfficer(p.Id, u.UserId()));

    /// <summary>Show the Compliance tab: a grant, the Auditor role, or any assigned point.</summary>
    public bool ShowsMenu(ClaimsPrincipal u, bool hasAssignments) => o.Enabled && (SeesAll(u) || hasAssignments);
}

/// <summary>Length and content rules for every compliance text field (DD-07 §3.1, "bounded text").</summary>
public static class ComplianceText
{
    public const int LongText = 4000, Remarks = 1000, Reference = 300, Short = 150;

    /// <summary>Trims and checks a field; returns null for blank optional fields.</summary>
    public static string? Clean(string label, string? value, int max, bool required = false)
    {
        var v = (value ?? "").Replace("\r\n", "\n").Trim();
        if (v.Length == 0)
        {
            if (required) throw new WorkflowException($"{label} is required.");
            return null;
        }
        if (v.Length > max) throw new WorkflowException($"{label} can be at most {max:N0} characters (it has {v.Length:N0}).");
        if (v.Any(ch => char.IsControl(ch) && ch != '\n' && ch != '\t')) throw new WorkflowException($"{label} contains characters that are not allowed.");
        // no single "word" over 200 characters: stops encoded files being pasted in as text
        if (v.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(w => w.Length > 200))
            throw new WorkflowException($"{label} contains a block of more than 200 characters without spaces. Files cannot be stored here — record where the papers are kept instead.");
        return v;
    }

    public static string Required(string label, string? value, int max) => Clean(label, value, max, true)!;
}
