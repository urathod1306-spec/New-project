using System.Security.Claims;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

// =====================================================================================================================
// Inward Register with Invoice Inward (release 1.7.0) — design: docs/designs/DD-08-inward-and-invoices.md
// This file: fixed codes, settings, records and the access rules. Queries: InwardRepository.cs; item transitions:
// InwardWorkflow.cs; invoice lifecycle: InvoiceWorkflow.cs; CSV imports: InwardImport.cs; prints: InwardReports.cs.
// =====================================================================================================================

/// <summary>Item types (fixed in code, DD-08 E2). "Invoice" has its own lifecycle.</summary>
public static class InwardType
{
    public const string Letter = "Letter", Email = "Email", Invoice = "Invoice", CreditDebitNote = "CreditDebitNote",
        GovtOrder = "GovtOrder", LegalNotice = "LegalNotice", Other = "Other";
    public static readonly Dictionary<string, string> Labels = new()
    {
        [Letter] = "Letter", [Email] = "E-mail", [Invoice] = "Invoice", [CreditDebitNote] = "Credit / debit note",
        [GovtOrder] = "Government order / notice", [LegalNotice] = "Legal notice", [Other] = "Other",
    };
    public static string Label(string? t) => t != null && Labels.TryGetValue(t, out var l) ? l : t ?? "";
}

public static class InwardMode
{
    public static readonly Dictionary<string, string> Labels = new()
    {
        ["Post"] = "Post", ["RegisteredPost"] = "Registered post", ["SpeedPost"] = "Speed Post", ["Courier"] = "Courier",
        ["Hand"] = "By hand", ["Email"] = "E-mail", ["Fax"] = "Fax", ["Portal"] = "Portal (GeM / e-procurement)",
    };
    public static string Label(string? m) => m != null && Labels.TryGetValue(m, out var l) ? l : m ?? "";
}

public static class InwardPriority
{
    public static readonly string[] All = { "Normal", "Urgent", "Immediate" };
}

public static class InwardAction
{
    public const string Reply = "Reply", Action = "Action", Information = "Information";
    public static readonly Dictionary<string, string> Labels = new() { [Reply] = "Reply", [Action] = "Action", [Information] = "Information only" };
}

/// <summary>Item status (DD-08 §3.2). Invoices map their own status onto these (§3.3).</summary>
public static class ItemStatus
{
    public const string Unassigned = "Unassigned", WithHolder = "WithHolder", WithAccounts = "WithAccounts", Closed = "Closed";
    public static string Label(string s) => s switch { WithHolder => "With holder", WithAccounts => "With Accounts", _ => s };
}

public static class Closure
{
    public const string Replied = "Replied", ActionCompleted = "ActionCompleted", Filed = "Filed", Forwarded = "Forwarded",
        ConvertedToENote = "ConvertedToENote", Duplicate = "Duplicate", Reminder = "Reminder", Cancelled = "Cancelled",
        InvoiceSettled = "InvoiceSettled", InvoiceClosedShort = "InvoiceClosedShort", InvoiceReturned = "InvoiceReturned", InvoiceRejected = "InvoiceRejected";
    public static readonly Dictionary<string, string> Labels = new()
    {
        [Replied] = "Replied", [ActionCompleted] = "Action completed", [Filed] = "Filed — no reply required", [Forwarded] = "Forwarded to another office / agency",
        [ConvertedToENote] = "Converted to e-Note", [Duplicate] = "Duplicate", [Reminder] = "Reminder of another item", [Cancelled] = "Cancelled — entered in error",
        [InvoiceSettled] = "Invoice paid", [InvoiceClosedShort] = "Invoice closed short", [InvoiceReturned] = "Invoice returned to vendor", [InvoiceRejected] = "Invoice rejected",
    };
    /// <summary>Closure types a holder may choose for a non-invoice item.</summary>
    public static readonly string[] ByHolder = { Replied, ActionCompleted, Filed, Forwarded, Duplicate, ConvertedToENote };
    public static string Label(string? c) => c != null && Labels.TryGetValue(c, out var l) ? l : c ?? "";
}

public static class InvStatus
{
    public const string Received = "Received", WithVerifier = "WithVerifier", Certified = "Certified", SentToAccounts = "SentToAccounts",
        PartPaid = "PartPaid", Paid = "Paid", OnHold = "OnHold", ReturnedToVendor = "ReturnedToVendor", Rejected = "Rejected",
        ClosedShort = "ClosedShort", Cancelled = "Cancelled";
    public static readonly string[] All = { Received, WithVerifier, Certified, SentToAccounts, PartPaid, Paid, OnHold, ReturnedToVendor, Rejected, ClosedShort, Cancelled };
    public static readonly string[] Final = { Paid, ClosedShort, ReturnedToVendor, Rejected, Cancelled };
    public const string FinalSql = "('Paid','ClosedShort','ReturnedToVendor','Rejected','Cancelled')";
    public static bool IsFinal(string s) => Final.Contains(s);

    /// <summary>The item status that goes with an invoice status (DD-08 §3.3).</summary>
    public static string ItemStatusFor(string s) => s switch
    {
        Received => ItemStatus.Unassigned,
        WithVerifier or Certified => ItemStatus.WithHolder,
        SentToAccounts or PartPaid => ItemStatus.WithAccounts,
        _ => ItemStatus.Closed,
    };

    public static string Label(string s) => s switch
    {
        WithVerifier => "With verifier", SentToAccounts => "Sent to Accounts", PartPaid => "Part-paid", OnHold => "On hold",
        ReturnedToVendor => "Returned to vendor", ClosedShort => "Closed short", _ => s,
    };
}

public static class InwardRole
{
    public const string Operator = "InwardOperator", Viewer = "InwardViewer", Accounts = "InvoiceAccounts";
    public static readonly Dictionary<string, string> Labels = new()
    {
        [Operator] = "Inward Operator (registry, per office)", [Viewer] = "Inward Viewer (sees all items)", [Accounts] = "Invoice Accounts (payments)",
    };
}

/// <summary>Settings from the "Inward" section of appsettings.json; every key is optional (DD-08 §9).</summary>
public sealed class InwardOptions
{
    public bool Enabled { get; set; } = true;
    public string NumberPrefix { get; set; } = "NCS/IN";
    public string DefaultOfficeName { get; set; } = "Mumbai (Head Office)";
    public bool SelfInwardEnabled { get; set; } = true;
    public int ReplyDueDaysNormal { get; set; } = 7;
    public int ReplyDueDaysUrgent { get; set; } = 3;
    public int ReplyDueDaysImmediate { get; set; } = 1;
    public int MaxBackdateDays { get; set; } = 90;
    public int UnassignedAlertWorkingDays { get; set; } = 1;
    public int NotAcceptedAlertDays { get; set; } = 2;
    public int OverdueRepeatDays { get; set; } = 3;
    public int InvoiceTermsDays { get; set; } = 30;
    public int MsmeMaxDays { get; set; } = 45;
    public int MsmeDueSoonDays { get; set; } = 5;
    public int InvoiceHolderAlertDays { get; set; } = 7;
    public int ReminderHour { get; set; } = 9;
    public bool ConvertedNotesAllowFiles { get; set; }
    public int MaxImportRows { get; set; } = 5000;
    public int MaxImportKB { get; set; } = 1900;

    public void Resolve()
    {
        if (string.IsNullOrWhiteSpace(NumberPrefix)) NumberPrefix = "NCS/IN";
        ReplyDueDaysNormal = Math.Clamp(ReplyDueDaysNormal, 0, 365);
        ReplyDueDaysUrgent = Math.Clamp(ReplyDueDaysUrgent, 0, 365);
        ReplyDueDaysImmediate = Math.Clamp(ReplyDueDaysImmediate, 0, 365);
        MaxBackdateDays = Math.Clamp(MaxBackdateDays, 0, 3650);
        OverdueRepeatDays = Math.Clamp(OverdueRepeatDays, 1, 90);
        InvoiceTermsDays = Math.Clamp(InvoiceTermsDays, 0, 365);
        MsmeMaxDays = Math.Clamp(MsmeMaxDays, 1, 365);
        ReminderHour = Math.Clamp(ReminderHour, 0, 23);
        MaxImportRows = Math.Clamp(MaxImportRows, 1, 20000);
        MaxImportKB = Math.Clamp(MaxImportKB, 16, 1900);         // the upload guard accepts at most 2 MB on import pages
    }

    public int ReplyDays(string priority) => priority switch { "Urgent" => ReplyDueDaysUrgent, "Immediate" => ReplyDueDaysImmediate, _ => ReplyDueDaysNormal };
}

// ------------------------------------------------------------------------------------------------- records

public sealed class InwardItem
{
    public long Id { get; set; }
    public string Number { get; set; } = "";
    public long OfficeId { get; set; }
    public string OfficeName { get; set; } = "";
    public string OfficeCode { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public long Serial { get; set; }
    public string ItemType { get; set; } = "";
    public string Mode { get; set; } = "";
    public string ReceivedAt { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string? SenderOrg { get; set; }
    public string? SenderRef { get; set; }
    public string? SenderRefDate { get; set; }
    public string Subject { get; set; } = "";
    public string? Summary { get; set; }
    public string? Category { get; set; }
    public string Priority { get; set; } = "Normal";
    public bool IsConfidential { get; set; }
    public string ActionRequired { get; set; } = "";
    public string? ReplyDueOn { get; set; }
    public string KeptAt { get; set; } = "";
    public long? Pages { get; set; }
    public string? Irn { get; set; }
    public string Status { get; set; } = "";
    public long? HolderUserId { get; set; }
    public string? HolderSince { get; set; }
    public string? AcceptedAt { get; set; }
    public long? LastHolderUserId { get; set; }
    public long InwardedByUserId { get; set; }
    public string InwardedByName { get; set; } = "";
    public string InwardedAt { get; set; } = "";
    public bool SelfInward { get; set; }
    public string? ClosureType { get; set; }
    public string? ClosureRemarks { get; set; }
    public string? ReplyDate { get; set; }
    public string? OutwardRef { get; set; }
    public string? FiledFileNo { get; set; }
    public string? ForwardedTo { get; set; }
    public long? DuplicateOfId { get; set; }
    public long? ENoteId { get; set; }
    public string? ClosedAt { get; set; }
    public string? ClosedByName { get; set; }
    public long? LinkedToId { get; set; }
    public long ReminderCount { get; set; }
    public string? LastDueReminderOn { get; set; }
    public string? LastOverdueReminderOn { get; set; }
    public long RowVersion { get; set; }
    public string UpdatedAt { get; set; } = "";
    // joined
    public string? HolderName { get; set; }
    public bool HolderActive { get; set; } = true;
    public string? HolderVertical { get; set; }
    public string? VendorName { get; set; }
    public string? InvoiceNo { get; set; }
    public string? InvoiceStatus { get; set; }
    public long? TotalPaise { get; set; }
    public long? AdjustPaise { get; set; }
    public long? SettledPaise { get; set; }
    public bool? IsMsme { get; set; }
    /// <summary>1 when the user has full access (participant, Inward Viewer, Accounts for invoices) — computed by the query.</summary>
    public bool Full { get; set; }

    public bool IsOpen => Status != ItemStatus.Closed;
    public bool IsInvoice => ItemType == InwardType.Invoice;
    /// <summary>Invoice Accounts see invoices and credit / debit notes (DD-08 R2-1).</summary>
    public bool IsAccountsType => ItemType is InwardType.Invoice or InwardType.CreditDebitNote;
    public bool Masked => IsConfidential && !Full;
    public string ShownSubject => Masked ? "Confidential" : Subject;
    public string ShownSender => Masked ? "Confidential" : SenderName + (string.IsNullOrEmpty(SenderOrg) ? "" : ", " + SenderOrg);
    public string? ShownSenderRef => Masked ? null : SenderRef;
    public string? ShownVendor => Masked ? "Confidential" : VendorName;
    public long? OutstandingPaise => TotalPaise is long t ? t + (AdjustPaise ?? 0) - (SettledPaise ?? 0) : null;
    public bool IsOverdue(DateOnly today) => IsOpen && !string.IsNullOrEmpty(ReplyDueOn) && string.CompareOrdinal(ReplyDueOn, today.ToString("yyyy-MM-dd")) < 0;
}

public sealed class InvoiceDetail
{
    public long ItemId { get; set; }
    public string VendorName { get; set; } = "";
    public string? VendorGstin { get; set; }
    public bool IsMsme { get; set; }
    public bool IsTrader { get; set; }
    public string? UdyamNo { get; set; }
    public string? MsmeClass { get; set; }
    public string InvoiceNo { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string InvoiceFy { get; set; } = "";
    public string? ProjectCode { get; set; }
    public string? PoRef { get; set; }
    public long? TaxablePaise { get; set; }
    public long? GstPaise { get; set; }
    public long? OtherPaise { get; set; }
    public long TotalPaise { get; set; }
    public long AdjustPaise { get; set; }
    public long SettledPaise { get; set; }
    public long TermsDays { get; set; }
    public string PaymentDueOn { get; set; } = "";
    public string InvoiceStatus { get; set; } = "";
    public string? HoldFromStatus { get; set; }
    public string? StatusReason { get; set; }
    public long? CertifiedByUserId { get; set; }
    public string? CertifiedByName { get; set; }
    public string? CertifiedOn { get; set; }
    public string? SentToAccountsAt { get; set; }
    public string? SettledAt { get; set; }
    public string DupKey { get; set; } = "";
    public string? OverrideReason { get; set; }
    public string? DuplicateCheckedBy { get; set; }
    public long EffectiveTotal => TotalPaise + AdjustPaise;
    public long Outstanding => EffectiveTotal - SettledPaise;
}

public sealed class InvoicePayment
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public string PaidOn { get; set; } = "";
    public long NetPaise { get; set; }
    public long TdsPaise { get; set; }
    public long OtherDeductionsPaise { get; set; }
    public string VoucherRef { get; set; } = "";
    public long? ReversesId { get; set; }
    public string? Remarks { get; set; }
    public string Source { get; set; } = "";
    public string EnteredByName { get; set; } = "";
    public string EnteredAt { get; set; } = "";
    public long Settled => NetPaise + TdsPaise + OtherDeductionsPaise;
    public bool Reversed { get; set; }
}

public sealed class InvoiceAdjustment
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public string Kind { get; set; } = "";
    public string DocNo { get; set; } = "";
    public string DocDate { get; set; } = "";
    public long AmountPaise { get; set; }
    public string? Remarks { get; set; }
    public string EnteredByName { get; set; } = "";
    public string EnteredAt { get; set; } = "";
}

public sealed class InwardMovement
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public string At { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public long? ToUserId { get; set; }
    public string? ToName { get; set; }
    public string? Remarks { get; set; }
}

public sealed class InwardNote
{
    public long Id { get; set; }
    public long ItemId { get; set; }
    public string At { get; set; } = "";
    public long UserId { get; set; }
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class InwardMember
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Role { get; set; } = "";
    public long OfficeId { get; set; }
    public string GrantedByName { get; set; } = "";
    public string GrantedAt { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Username { get; set; } = "";
    public bool IsActive { get; set; }
    public string? OfficeName { get; set; }
}

public sealed class InwardMemberChange
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Action { get; set; } = "";
    public string? Role { get; set; }
    public long? OfficeId { get; set; }
    public string ActorName { get; set; } = "";
    public string At { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class ProjectCode
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public long? OwnerUserId { get; set; }
    public string? OwnerName { get; set; }
    public bool IsActive { get; set; }
    public string UpdatedAt { get; set; } = "";
    public string UpdatedBy { get; set; } = "";
}

// ------------------------------------------------------------------------------------------------- access rules

/// <summary>
/// Who may see and do what in the inward register (DD-08 §5). Grants (roles × offices) are cached for at most 30 seconds
/// and reloaded after every change. "Visible" and "full access" are SQL predicates used by every list, count and export.
/// </summary>
public sealed class InwardAccess(Db db, InwardOptions o)
{
    private sealed record Grant(string Role, long OfficeId);
    private volatile Dictionary<long, List<Grant>>? _grants;
    private long _loaded;
    private static readonly long Ttl = TimeSpan.FromSeconds(30).Ticks;

    public InwardOptions Options => o;

    private Dictionary<long, List<Grant>> Grants
    {
        get
        {
            var g = _grants;
            if (g != null && DateTime.UtcNow.Ticks - Interlocked.Read(ref _loaded) < Ttl) return g;
            g = db.Query<InwardMember>("SELECT m.UserId, m.Role, m.OfficeId FROM InwardMembers m JOIN Users u ON u.Id = m.UserId WHERE u.IsActive = 1")
                .GroupBy(m => m.UserId).ToDictionary(x => x.Key, x => x.Select(m => new Grant(m.Role, m.OfficeId)).ToList());
            Interlocked.Exchange(ref _loaded, DateTime.UtcNow.Ticks);
            _grants = g;
            return g;
        }
    }

    public void Invalidate() => _grants = null;

    private List<Grant> Of(ClaimsPrincipal u) => Grants.TryGetValue(u.UserId(), out var l) ? l : new();
    public bool IsViewer(ClaimsPrincipal u) => o.Enabled && Of(u).Any(g => g.Role == InwardRole.Viewer);
    public bool IsAccounts(ClaimsPrincipal u) => o.Enabled && Of(u).Any(g => g.Role == InwardRole.Accounts);
    public bool IsAuditor(ClaimsPrincipal u) => o.Enabled && u.IsAuditor();
    public List<long> OperatorOffices(ClaimsPrincipal u) => o.Enabled ? Of(u).Where(g => g.Role == InwardRole.Operator).Select(g => g.OfficeId).ToList() : new();
    public bool IsOperator(ClaimsPrincipal u) => OperatorOffices(u).Count > 0;
    public bool IsOperatorOf(ClaimsPrincipal u, long officeId) => OperatorOffices(u).Contains(officeId);
    /// <summary>Sees the register and dashboard (some or all items).</summary>
    public bool SeesRegister(ClaimsPrincipal u) => IsViewer(u) || IsAccounts(u) || IsAuditor(u) || IsOperator(u);
    public IReadOnlyList<long> AccountsIds() => Grants.Where(g => g.Value.Any(x => x.Role == InwardRole.Accounts)).Select(g => g.Key).ToList();
    public IReadOnlyList<long> ViewerIds() => Grants.Where(g => g.Value.Any(x => x.Role == InwardRole.Viewer)).Select(g => g.Key).ToList();
    public IReadOnlyList<long> OperatorIds(long officeId) => Grants.Where(g => g.Value.Any(x => x.Role == InwardRole.Operator && x.OfficeId == officeId)).Select(g => g.Key).ToList();
    /// <summary>Part of the My Desk cache key: the user's grants in a fixed order.</summary>
    public string RoleKey(ClaimsPrincipal u) => string.Join(",", Of(u).Select(g => g.Role + ":" + g.OfficeId).OrderBy(x => x, StringComparer.Ordinal));

    private const string Participant = "EXISTS (SELECT 1 FROM InwardParticipants ip WHERE ip.ItemId = {0}.Id AND ip.UserId = @uid)";

    /// <summary>SQL: the user (@uid) may see the item aliased <paramref name="i"/> (full or metadata only).</summary>
    public string VisibleSql(ClaimsPrincipal u, string i = "i")
    {
        if (IsViewer(u) || IsAuditor(u)) return "1 = 1";
        var parts = new List<string> { string.Format(Participant, i) };
        var offices = OperatorOffices(u);
        if (offices.Count > 0) parts.Add($"{i}.OfficeId IN ({string.Join(",", offices)})");
        if (IsAccounts(u)) parts.Add($"{i}.ItemType IN ('Invoice','CreditDebitNote')");
        return "(" + string.Join(" OR ", parts) + ")";
    }

    /// <summary>SQL: the user has full access (participant, Inward Viewer, Accounts for invoices) to item <paramref name="i"/>.</summary>
    public string FullSql(ClaimsPrincipal u, string i = "i")
    {
        if (IsViewer(u)) return "1 = 1";
        var parts = new List<string> { string.Format(Participant, i) };
        if (IsAccounts(u)) parts.Add($"{i}.ItemType IN ('Invoice','CreditDebitNote')");
        return "(" + string.Join(" OR ", parts) + ")";
    }

    public static bool IsParticipant(SqliteConnection c, SqliteTransaction? tx, long itemId, long userId) =>
        Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM InwardParticipants WHERE ItemId = @itemId AND UserId = @userId", new { itemId, userId }) > 0;

    public bool IsParticipant(long itemId, long userId) =>
        db.Scalar<long>("SELECT COUNT(*) FROM InwardParticipants WHERE ItemId = @itemId AND UserId = @userId", new { itemId, userId }) > 0;

    public bool CanView(InwardItem it, ClaimsPrincipal u) =>
        o.Enabled && (IsViewer(u) || IsAuditor(u) || IsOperatorOf(u, it.OfficeId) || (IsAccounts(u) && it.IsAccountsType) || IsParticipant(it.Id, u.UserId()));

    public bool HasFull(InwardItem it, ClaimsPrincipal u) =>
        IsViewer(u) || (IsAccounts(u) && it.IsAccountsType) || IsParticipant(it.Id, u.UserId());

    /// <summary>Show the Inward tab: any grant, the Auditor role, self-inward, or a participant in any item.</summary>
    public bool ShowsMenu(ClaimsPrincipal u, bool participant) => o.Enabled && (SeesRegister(u) || u.IsAdmin() || o.SelfInwardEnabled || participant);
}
