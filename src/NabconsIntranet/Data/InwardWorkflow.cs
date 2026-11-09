using System.Globalization;
using System.Security.Claims;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

/// <summary>The inward form (DD-08 §6.3). Text is checked on the server; the invoice part is used only for Type = Invoice.</summary>
public sealed class InwardInput
{
    public long OfficeId { get; set; }
    public string? ItemType { get; set; }
    public string? Mode { get; set; }
    public string? ReceivedOn { get; set; }
    public string? ReceivedTime { get; set; }
    public string? SenderName { get; set; }
    public string? SenderOrg { get; set; }
    public string? SenderRef { get; set; }
    public string? SenderRefDate { get; set; }
    public string? Subject { get; set; }
    public string? Summary { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public bool IsConfidential { get; set; }
    public string? ActionRequired { get; set; }
    public string? ReplyDueOn { get; set; }
    public string? KeptAt { get; set; }
    public long? Pages { get; set; }
    public string? Irn { get; set; }
    /// <summary>Reminder / follow-up of this inward number; for a credit / debit note, the invoice it is against.</summary>
    public string? LinkedNumber { get; set; }
    public long? MarkToUserId { get; set; }
    public List<long> CopyToIds { get; set; } = new();
    public bool ConfirmConfidentialCopy { get; set; }
    public InvoiceInput Invoice { get; set; } = new();
}

public sealed class CloseInput
{
    public string? ClosureType { get; set; }
    public string? Remarks { get; set; }
    public string? ReplyDate { get; set; }
    public string? OutwardRef { get; set; }
    public string? FiledFileNo { get; set; }
    public string? ForwardedTo { get; set; }
    public string? DuplicateOfNumber { get; set; }
}

/// <summary>Raised when a save needs the user's confirmation (warnings), or an override reason (a blocked duplicate).</summary>
public sealed class InwardConfirmException(List<string> warnings, bool blocked) : Exception(string.Join(" ", warnings))
{
    public List<string> Warnings { get; } = warnings;
    /// <summary>True for a duplicate invoice: saving needs "This is not a duplicate" and a reason.</summary>
    public bool Blocked { get; } = blocked;
}

public sealed class TransferPreview
{
    public long Items { get; set; }
    public long Invoices { get; set; }
    public long SkippedInvoices { get; set; }
}

/// <summary>
/// Every change to the inward register (DD-08 §3.2; invoice transitions are in InvoiceWorkflow.cs, the other half of this
/// class). Each action is one BEGIN IMMEDIATE transaction that checks and bumps RowVersion and writes one movement;
/// notifications go in the same transaction, the audit entry (number and type only) after commit.
/// </summary>
public sealed partial class InwardWorkflow(Db db, InwardAccess access, InwardRepository repo, InwardOptions o, ENoteOptions enoteOptions,
    ENoteWorkflow enotes, MasterRepository masters, AuditService audit, INotificationService notifications, ChangeTracker tracker, Fmt fmt)
{
    private const string Changed = "This item was changed by someone else. Please reload the page and try again.";
    private const string NotFound = "Inward item not found.";
    private static readonly NotificationActor SystemActor = new(0, "NABCONS Intranet");

    private string Today => fmt.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ================================================================= helpers

    internal static string? Date(string label, string? v, bool required = false) => ComplianceWorkflow.ParseDate(label, v, required);
    private static string? T(string label, string? v, int max, bool required = false) => ComplianceText.Clean(label, v, max, required);

    internal const string LoadSql = """
        SELECT i.*, h.DisplayName AS HolderName, COALESCE(h.IsActive, 1) AS HolderActive, h.Vertical AS HolderVertical,
               d.VendorName, d.InvoiceNo, d.InvoiceStatus, d.TotalPaise, d.AdjustPaise, d.SettledPaise, d.IsMsme, 1 AS Full
        FROM InwardItems i LEFT JOIN Users h ON h.Id = i.HolderUserId LEFT JOIN InvoiceDetails d ON d.ItemId = i.Id
        """;

    private static InwardItem Load(SqliteConnection c, SqliteTransaction tx, long id) =>
        Db.Query<InwardItem>(c, tx, LoadSql + " WHERE i.Id = @id", new { id }).FirstOrDefault() ?? throw new WorkflowException(NotFound);

    /// <summary>Loads the item and checks the user may see it; anything not visible is "not found" (§5).</summary>
    private InwardItem LoadVisible(SqliteConnection c, SqliteTransaction tx, long id, ClaimsPrincipal u)
    {
        var it = Load(c, tx, id);
        if (!access.CanView(it, u)) throw new WorkflowException(NotFound);
        return it;
    }

    private static void Bump(SqliteConnection c, SqliteTransaction tx, InwardItem it, long rowVersion, string set, Dictionary<string, object?> args)
    {
        args["Id"] = it.Id; args["rv"] = rowVersion; args["now"] = Db.Now();
        var n = Db.Exec(c, tx, $"UPDATE InwardItems SET {set}, RowVersion = RowVersion + 1, UpdatedAt = @now WHERE Id = @Id AND RowVersion = @rv", args);
        if (n == 0) throw new WorkflowException(Changed);
    }

    private static void Move(SqliteConnection c, SqliteTransaction tx, long itemId, ClaimsPrincipal? u, string action, long? toUserId = null, string? toName = null, string? remarks = null) =>
        Db.Exec(c, tx, """
            INSERT INTO InwardMovements (ItemId, At, ActorUserId, ActorName, Action, ToUserId, ToName, Remarks)
            VALUES (@itemId, @at, @uid, @name, @action, @toUserId, @toName, @remarks)
            """, new { itemId, at = Db.Now(), uid = u?.UserId(), name = u?.DisplayName() ?? "system", action, toUserId, toName, remarks });

    private static void AddParticipant(SqliteConnection c, SqliteTransaction tx, long itemId, long userId) =>
        Db.Exec(c, tx, "INSERT OR IGNORE INTO InwardParticipants (ItemId, UserId) VALUES (@itemId, @userId)", new { itemId, userId });

    /// <summary>Text search index (§4). A confidential item is indexed by its number and invoice number only (E8).</summary>
    internal static void Index(SqliteConnection c, SqliteTransaction tx, long id)
    {
        var it = Load(c, tx, id);
        Db.Exec(c, tx, "DELETE FROM InwardSearch WHERE rowid = @id", new { id });
        var conf = it.IsConfidential;
        Db.Exec(c, tx, """
            INSERT INTO InwardSearch (rowid, Number, Sender, SenderRef, Subject, Summary, Vendor, InvoiceNo)
            VALUES (@id, @number, @sender, @senderRef, @subject, @summary, @vendor, @invoiceNo)
            """, new
        {
            id, number = it.Number,
            sender = conf ? null : it.SenderName + " " + it.SenderOrg, senderRef = conf ? null : it.SenderRef,
            subject = conf ? null : it.Subject, summary = conf ? null : it.Summary, vendor = conf ? null : it.VendorName, invoiceNo = it.InvoiceNo,
        });
    }

    private static string? UserName(SqliteConnection c, SqliteTransaction tx, long id) =>
        Db.Scalar<string>(c, tx, "SELECT DisplayName FROM Users WHERE Id = @id", new { id });

    private static string ActiveUser(SqliteConnection c, SqliteTransaction tx, long? id, string what)
    {
        if (id is not > 0) throw new WorkflowException($"Choose {what}.");
        return Db.Scalar<string>(c, tx, "SELECT DisplayName FROM Users WHERE Id = @id AND IsActive = 1", new { id })
               ?? throw new WorkflowException($"Choose {what} from the list of active users.");
    }

    private NotificationMessage Msg(InwardItem it, string type, string what) =>
        new("Inward." + type, $"Inward {it.Number}: {what}", InwardType.Label(it.ItemType), $"/Inward/Item/{it.Id}", "InwardItem", it.Id);

    private void Audit(string action, InwardItem it, string? detail = null) =>
        audit.Log("Inward." + action, "InwardItem", it.Id, $"{it.Number}; {InwardType.Label(it.ItemType)}" + (detail == null ? "" : "; " + detail));

    private void Touch(params long?[] users)
    {
        foreach (var id in users.Where(x => x > 0).Distinct()) tracker.TouchUser(id!.Value);
        tracker.Touch(ChangeTracker.InwardQueue);
    }

    private static T InTx<T>(Db db, Func<SqliteConnection, SqliteTransaction, T> f)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var r = f(c, tx);
        tx.Commit();
        return r;
    }

    private static List<long> Copies(SqliteConnection c, SqliteTransaction tx, long itemId) =>
        Db.Query<IdRow>(c, tx, "SELECT UserId AS Id FROM InwardCopies WHERE ItemId = @itemId", new { itemId }).Select(r => r.Id).ToList();

    private sealed class IdRow { public long Id { get; set; } }

    private static void RequireOpen(InwardItem it)
    {
        if (!it.IsOpen) throw new WorkflowException("This item is closed; it must be reopened first.");
    }

    private bool IsOp(ClaimsPrincipal u, InwardItem it) => access.IsOperatorOf(u, it.OfficeId);
    private static bool IsHolder(ClaimsPrincipal u, InwardItem it) => it.HolderUserId == u.UserId() && it.Status == ItemStatus.WithHolder;

    /// <summary>"Received at": the office date and optional time, stored as UTC ISO; not in the future, not before the backdate limit.</summary>
    private string ReceivedUtc(string? day, string? time)
    {
        var d = DateOnly.ParseExact(Date("Received on", day, true)!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var t = TimeOnly.MinValue;
        if (!string.IsNullOrWhiteSpace(time) && !TimeOnly.TryParseExact(time.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
            throw new WorkflowException("Received time: enter it as HH:mm (24-hour).");
        if (string.IsNullOrWhiteSpace(time) && d == fmt.Today) t = TimeOnly.FromDateTime(fmt.LocalNow);
        if (d > fmt.Today) throw new WorkflowException("The received date cannot be in the future.");
        if (d < fmt.Today.AddDays(-o.MaxBackdateDays)) throw new WorkflowException($"The received date can be at most {o.MaxBackdateDays} days ago.");
        var local = d.ToDateTime(t);
        if (local > fmt.LocalNow.AddMinutes(5)) throw new WorkflowException("The received time cannot be in the future.");
        return DateTime.SpecifyKind(local.AddMinutes(-fmt.OffsetMinutes), DateTimeKind.Utc).ToString("o", CultureInfo.InvariantCulture);
    }

    /// <summary>The office date of a stored UTC time.</summary>
    internal DateOnly OfficeDate(string isoUtc) =>
        DateOnly.FromDateTime(DateTime.Parse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal).AddMinutes(fmt.OffsetMinutes));

    /// <summary>Validated item fields shared by Inward and Edit.</summary>
    private sealed class Clean
    {
        public string Mode = "", ReceivedAt = "", SenderName = "", Subject = "", Priority = "Normal", ActionRequired = "", KeptAt = "";
        public string? SenderOrg, SenderRef, SenderRefDate, Summary, Category, ReplyDueOn, Irn;
        public bool IsConfidential;
        public long? Pages;
    }

    private Clean Validate(InwardInput i, string type, string? keepReceivedAt = null)
    {
        var r = new Clean();
        r.Mode = i.Mode != null && InwardMode.Labels.ContainsKey(i.Mode) ? i.Mode : throw new WorkflowException("Choose how the item was received.");
        r.ReceivedAt = keepReceivedAt ?? ReceivedUtc(i.ReceivedOn, i.ReceivedTime);
        r.SenderName = T("Sender name", i.SenderName, 150, true)!;
        r.SenderOrg = T("Sender's organisation", i.SenderOrg, 200);
        r.SenderRef = T("Sender's reference", i.SenderRef, 100);
        r.SenderRefDate = Date("Sender's reference date", i.SenderRefDate);
        if (r.SenderRefDate != null && string.CompareOrdinal(r.SenderRefDate, Today) > 0) throw new WorkflowException("The sender's reference date cannot be in the future.");
        r.Subject = T("Subject", i.Subject, 300, true)!;
        r.Summary = T("Summary", i.Summary, 2000);
        r.Category = T("Category", i.Category, 150);
        if (r.Category != null && !masters.Names(MasterTypes.InwardCategory).Contains(r.Category)) throw new WorkflowException("Choose a category from the list.");
        r.Priority = InwardPriority.All.Contains(i.Priority) ? i.Priority! : "Normal";
        r.IsConfidential = i.IsConfidential;
        r.KeptAt = T("Where the papers are kept", i.KeptAt, 300, true)!;
        if (i.Pages is < 0 or > 10000) throw new WorkflowException("Pages must be between 0 and 10,000.");
        r.Pages = i.Pages;
        r.Irn = T("IRN", i.Irn, 64);
        if (r.Irn != null && (r.Irn.Length != 64 || !r.Irn.All(char.IsAsciiHexDigit))) throw new WorkflowException("The IRN of an e-invoice is 64 letters and digits (0–9, a–f).");
        if (type == InwardType.Invoice)
        {
            r.ActionRequired = InwardAction.Action;                      // invoices have their own alerts (§3.3)
            r.ReplyDueOn = null;
        }
        else
        {
            r.ActionRequired = i.ActionRequired != null && InwardAction.Labels.ContainsKey(i.ActionRequired) ? i.ActionRequired : throw new WorkflowException("Choose the action required.");
            if (r.ActionRequired == InwardAction.Information) r.ReplyDueOn = null;        // R2-2
            else
            {
                var received = OfficeDate(r.ReceivedAt);
                r.ReplyDueOn = Date("Reply due", i.ReplyDueOn)
                               ?? received.AddDays(o.ReplyDays(r.Priority)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (string.CompareOrdinal(r.ReplyDueOn, received.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0)
                    throw new WorkflowException("The reply-due date cannot be before the received date.");
            }
        }
        return r;
    }

    /// <summary>The item the number refers to, if the user may see it; links are followed to the root item (E4).</summary>
    private InwardItem? Linked(SqliteConnection c, SqliteTransaction tx, string? number, ClaimsPrincipal u)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;
        var it = Db.Query<InwardItem>(c, tx, LoadSql + " WHERE i.Number = @n COLLATE NOCASE", new { n = number.Trim() }).FirstOrDefault();
        if (it == null || !access.CanView(it, u)) throw new WorkflowException($"Inward number {number.Trim()} was not found.");
        for (var hops = 0; it.LinkedToId != null && it.ClosureType == Closure.Reminder && hops < 50; hops++)
            it = Load(c, tx, it.LinkedToId.Value);
        return it;
    }

    private (long next, string number) NextNumber(SqliteConnection c, SqliteTransaction tx, string code, string fy)
    {
        var serial = Db.Scalar<long>(c, tx, """
            INSERT INTO InwardSerials (OfficeCode, FinancialYear, LastSerial) VALUES (@code, @fy, 1)
            ON CONFLICT (OfficeCode, FinancialYear) DO UPDATE SET LastSerial = LastSerial + 1 RETURNING LastSerial
            """, new { code, fy });
        return (serial, $"{o.NumberPrefix}/{code}/{fy}/{serial:D5}");
    }

    // ================================================================= inward

    /// <summary>
    /// Inwards an item (registry or self-inward). Warnings (§3.5) raise <see cref="InwardConfirmException"/> unless
    /// <paramref name="confirmed"/>; a duplicate invoice needs an override reason whatever is confirmed.
    /// </summary>
    public long Inward(InwardInput i, ClaimsPrincipal u, bool confirmed)
    {
        if (!o.Enabled) throw new WorkflowException("The inward register is switched off.");
        var type = i.ItemType != null && InwardType.Labels.ContainsKey(i.ItemType) ? i.ItemType : throw new WorkflowException("Choose the type of item.");
        var office = repo.Office(i.OfficeId);
        var registry = office != null && office.IsActive && access.IsOperatorOf(u, office.Id);
        if (!registry)
        {
            if (!o.SelfInwardEnabled) throw new WorkflowException("Only the registry of the office can inward items.");
            office = repo.OwnOffice(u) ?? (office is { IsActive: true } ? office : null)
                     ?? throw new WorkflowException("Choose the office where the item was received.");
        }
        var v = Validate(i, type);
        var isInvoice = type == InwardType.Invoice;
        var inv = isInvoice ? ValidateInvoice(i.Invoice, null) : null;
        var uid = u.UserId();

        var (id, it, notify) = InTx(db, (c, tx) =>
        {
            // routing
            long? holder = i.MarkToUserId > 0 ? i.MarkToUserId : (!registry && !isInvoice ? uid : null);
            string? holderName = holder is long h ? ActiveUser(c, tx, h, "the officer to mark it to") : null;
            if (isInvoice && holder == uid) throw new WorkflowException("An invoice cannot be marked to the person inwarding it; choose another officer or leave it with the registry.");

            // reminder / credit-note link
            InwardItem? link = null;
            var isReminder = false;
            if (!string.IsNullOrWhiteSpace(i.LinkedNumber))
            {
                link = Linked(c, tx, i.LinkedNumber, u);
                if (type == InwardType.CreditDebitNote)
                {
                    if (!link!.IsInvoice) throw new WorkflowException("A credit / debit note must be against an inwarded invoice.");
                }
                else isReminder = true;
            }
            else if (type == InwardType.CreditDebitNote) throw new WorkflowException("Enter the inward number of the invoice this credit / debit note is against.");
            if (isReminder && isInvoice) throw new WorkflowException("An invoice cannot be inwarded as a reminder; inward it as a new invoice.");

            var conf = isReminder ? link!.IsConfidential : v.IsConfidential;
            var warnings = new List<string>();
            if (!confirmed && v.SenderRef != null && v.SenderRefDate != null)
            {
                var same = Db.Scalar<string>(c, tx, "SELECT Number FROM InwardItems WHERE SenderRef = @r COLLATE NOCASE AND SenderRefDate = @d AND ClosureType IS NOT 'Cancelled' LIMIT 1",
                    new { r = v.SenderRef, d = v.SenderRefDate });
                if (same != null) warnings.Add($"An item with the same sender's reference and date is already inwarded as {same}.");
            }
            var fy = LeadRepository.FinancialYear(fmt.Today);
            if (inv != null) warnings.AddRange(DuplicateCheck(c, tx, inv, null, confirmed));
            if (warnings.Count > 0) throw new InwardConfirmException(warnings, false);

            var (serial, number) = NextNumber(c, tx, office!.NumberCode, fy);
            var now = Db.Now();
            var status = isReminder ? ItemStatus.Closed : holder != null ? ItemStatus.WithHolder : ItemStatus.Unassigned;
            var newId = Db.Insert(c, tx, """
                INSERT INTO InwardItems (Number, OfficeId, OfficeName, OfficeCode, FinancialYear, Serial, ItemType, Mode, ReceivedAt, SenderName, SenderOrg,
                    SenderRef, SenderRefDate, Subject, Summary, Category, Priority, IsConfidential, ActionRequired, ReplyDueOn, KeptAt, Pages, Irn, Status,
                    HolderUserId, HolderSince, AcceptedAt, LastHolderUserId, InwardedByUserId, InwardedByName, InwardedAt, SelfInward, ClosureType, ClosureRemarks,
                    ClosedAt, ClosedByName, LinkedToId, UpdatedAt)
                VALUES (@number, @officeId, @officeName, @code, @fy, @serial, @type, @Mode, @ReceivedAt, @SenderName, @SenderOrg,
                    @SenderRef, @SenderRefDate, @Subject, @Summary, @Category, @Priority, @conf, @ActionRequired, @reply, @KeptAt, @Pages, @Irn, @status,
                    @holder, @since, @accepted, @holder, @uid, @uname, @now, @self, @closure, @closureRemarks,
                    @closedAt, @closedBy, @linkId, @now)
                """, new
            {
                number, officeId = office.Id, officeName = office.Name, code = office.NumberCode, fy, serial, type, v.Mode, v.ReceivedAt, v.SenderName, v.SenderOrg,
                v.SenderRef, v.SenderRefDate, v.Subject, v.Summary, v.Category, v.Priority, conf, v.ActionRequired,
                reply = isReminder ? null : v.ReplyDueOn, v.KeptAt, v.Pages, v.Irn, status,
                holder = isReminder ? (long?)null : holder, since = holder != null && !isReminder ? now : null,
                accepted = holder == uid && !isReminder ? now : null,
                uid, uname = u.DisplayName(), now, self = !registry,
                closure = isReminder ? Closure.Reminder : null, closureRemarks = isReminder ? $"Reminder / follow-up of {link!.Number}" : null,
                closedAt = isReminder ? now : null, closedBy = isReminder ? u.DisplayName() : null, linkId = link?.Id,
            });
            AddParticipant(c, tx, newId, uid);
            Move(c, tx, newId, u, registry ? "Inwarded" : "SelfInwarded");
            if (inv != null) InsertInvoice(c, tx, newId, inv, holder != null ? InvStatus.WithVerifier : InvStatus.Received);
            var created = Load(c, tx, newId);
            var notify = new List<long?>();
            if (holder is long hid && !isReminder)
            {
                AddParticipant(c, tx, newId, hid);
                Move(c, tx, newId, u, "Marked", hid, holderName);
                notifications.Notify(c, tx, new[] { hid }, NotificationActor.From(u), Msg(created, "Marked", "marked to you"));
                notify.Add(hid);
            }
            if (isReminder) notify.AddRange(ReminderArrived(c, tx, link!, created, u));
            notify.AddRange(AddCopies(c, tx, created, i.CopyToIds, i.ConfirmConfidentialCopy, u));
            Index(c, tx, newId);
            return (newId, created, notify);
        });
        Touch(notify.Append(uid).ToArray());
        Audit(registry ? "Inwarded" : "SelfInwarded", it, (it.IsConfidential ? "confidential" : null) + (it.LinkedToId != null ? $" linked to #{it.LinkedToId}" : ""));
        if (inv?.OverrideReason != null) audit.Log("Inward.DuplicateOverride", "InwardItem", id, $"{it.Number}; override of an existing invoice");
        return id;
    }

    /// <summary>A reminder arrived for <paramref name="orig"/> (E4): count it, reopen a closed non-invoice item, notify.</summary>
    private List<long?> ReminderArrived(SqliteConnection c, SqliteTransaction tx, InwardItem orig, InwardItem reminder, ClaimsPrincipal u)
    {
        var reopen = !orig.IsOpen && !orig.IsInvoice && orig.ClosureType is not (Closure.Cancelled or Closure.Reminder or Closure.ConvertedToENote);
        var set = "ReminderCount = ReminderCount + 1";
        var args = new Dictionary<string, object?>();
        long? to = null;
        if (reopen)
        {
            to = orig.LastHolderUserId is long lh && Db.Scalar<long>(c, tx, "SELECT IsActive FROM Users WHERE Id = @lh", new { lh }) == 1 ? lh : null;
            set += ", Status = @st, HolderUserId = @to, HolderSince = CASE WHEN @to IS NULL THEN NULL ELSE @now END, AcceptedAt = NULL, " +
                   "ClosureType = NULL, ClosureRemarks = NULL, ClosedAt = NULL, ClosedByName = NULL";
            args["st"] = to == null ? ItemStatus.Unassigned : ItemStatus.WithHolder;
            args["to"] = to;
        }
        Bump(c, tx, orig, orig.RowVersion, set, args);
        Move(c, tx, orig.Id, u, "ReminderReceived", remarks: reminder.Number);
        if (reopen) Move(c, tx, orig.Id, u, "Reopened", to, to is long t ? UserName(c, tx, t) : null, "Reopened by a reminder");
        var cur = Load(c, tx, orig.Id);
        var recipients = cur.Status switch
        {
            ItemStatus.WithHolder => new List<long> { cur.HolderUserId!.Value },
            ItemStatus.WithAccounts => access.AccountsIds().ToList(),
            ItemStatus.Unassigned => access.OperatorIds(cur.OfficeId).ToList(),
            _ => cur.IsInvoice ? access.AccountsIds().ToList() : new List<long>(),        // R2-17
        };
        notifications.Notify(c, tx, recipients, NotificationActor.From(u), Msg(cur, "FollowUp", $"reminder received ({reminder.Number})"));
        return recipients.Select(x => (long?)x).ToList();
    }

    private List<long?> AddCopies(SqliteConnection c, SqliteTransaction tx, InwardItem it, IEnumerable<long> ids, bool confirmConfidential, ClaimsPrincipal u)
    {
        var list = ids.Where(x => x > 0).Distinct().ToList();
        if (list.Count == 0) return new();
        if (list.Count > 50) throw new WorkflowException("Copy to at most 50 people at a time.");
        if (it.IsConfidential && !confirmConfidential) throw new WorkflowException("This item is confidential. Tick the confirmation to copy it to other people.");
        var existing = Copies(c, tx, it.Id).ToHashSet();
        var added = new List<long>();
        foreach (var id in list.Where(x => !existing.Contains(x)))
        {
            var name = ActiveUser(c, tx, id, "people to copy to");
            Db.Exec(c, tx, "INSERT INTO InwardCopies (ItemId, UserId, AddedAt, AddedByName) VALUES (@itemId, @id, @now, @by)",
                new { itemId = it.Id, id, now = Db.Now(), by = u.DisplayName() });
            AddParticipant(c, tx, it.Id, id);
            Move(c, tx, it.Id, u, "Copied", id, name);
            added.Add(id);
        }
        if (added.Count > 0) notifications.Notify(c, tx, added, NotificationActor.From(u), Msg(it, "Copied", "copied to you"));
        return added.Select(x => (long?)x).ToList();
    }

    // ================================================================= routing

    /// <summary>Mark an unassigned item, or re-mark a held one (§3.2, §3.3).</summary>
    public void Mark(long id, long toUserId, string? remarks, long rowVersion, ClaimsPrincipal u)
    {
        var rem = T("Remarks", remarks, 1000);
        var (it, prev) = InTx(db, (c, tx) =>
        {
            var it = LoadVisible(c, tx, id, u);
            var name = ActiveUser(c, tx, toUserId, "the officer to mark it to");
            var inv = it.IsInvoice ? InvoiceOf(c, tx, it.Id) : null;
            if (inv?.InvoiceStatus == InvStatus.OnHold) throw new WorkflowException("This invoice is on hold; release it first.");
            if (it.Status == ItemStatus.Unassigned)
            {
                if (!IsOp(u, it) && !access.IsViewer(u)) throw new WorkflowException("Only the registry of the office or an Inward Viewer can mark this item.");
            }
            else if (it.Status == ItemStatus.WithHolder)
            {
                if (!IsHolder(u, it) && !IsOp(u, it) && !access.IsViewer(u)) throw new WorkflowException("Only the holder, the registry of the office or an Inward Viewer can re-mark this item.");
                if (it.HolderUserId == toUserId) throw new WorkflowException("The item is already with this officer.");
            }
            else throw new WorkflowException(it.Status == ItemStatus.Closed ? "This item is closed; it must be reopened first." : "This invoice is with Accounts.");
            if (it.IsInvoice && toUserId == it.InwardedByUserId) throw new WorkflowException("An invoice cannot be marked to the person who inwarded it.");
            var prev = it.HolderUserId;
            Bump(c, tx, it, rowVersion, "Status = 'WithHolder', HolderUserId = @to, HolderSince = @now2, AcceptedAt = NULL, LastHolderUserId = @to",
                new() { ["to"] = toUserId, ["now2"] = Db.Now() });
            if (inv != null)
                Db.Exec(c, tx, "UPDATE InvoiceDetails SET InvoiceStatus = 'WithVerifier', CertifiedByUserId = NULL, CertifiedByName = NULL, CertifiedOn = NULL WHERE ItemId = @id", new { id });
            AddParticipant(c, tx, id, toUserId);
            Move(c, tx, id, u, prev == null ? "Marked" : "Remarked", toUserId, name, rem);
            notifications.Notify(c, tx, new[] { toUserId }, NotificationActor.From(u), Msg(it, "Marked", "marked to you"));
            return (it, prev);
        });
        Touch(toUserId, prev, u.UserId());
        Audit(prev == null ? "Marked" : "Remarked", it, $"to user #{toUserId}");
    }

    public void Accept(long id, long rowVersion, ClaimsPrincipal u)
    {
        var it = InTx(db, (c, tx) =>
        {
            var it = LoadVisible(c, tx, id, u);
            if (!IsHolder(u, it)) throw new WorkflowException("Only the holder can accept this item.");
            if (it.AcceptedAt != null) throw new WorkflowException("You have already accepted this item.");
            Bump(c, tx, it, rowVersion, "AcceptedAt = @now", new());
            Move(c, tx, id, u, "Accepted");
            return it;
        });
        Touch(u.UserId());
        Audit("Accepted", it);
    }

    public void ReturnToRegistry(long id, string? remarks, long rowVersion, ClaimsPrincipal u)
    {
        var rem = T("Remarks", remarks, 1000, true);
        var it = InTx(db, (c, tx) =>
        {
            var it = LoadVisible(c, tx, id, u);
            if (!IsHolder(u, it)) throw new WorkflowException("Only the holder can return this item to the registry.");
            var inv = it.IsInvoice ? InvoiceOf(c, tx, it.Id) : null;
            if (inv?.InvoiceStatus == InvStatus.OnHold) throw new WorkflowException("This invoice is on hold; release it first.");
            Bump(c, tx, it, rowVersion, "Status = 'Unassigned', HolderUserId = NULL, HolderSince = NULL, AcceptedAt = NULL", new());
            if (inv != null)
                Db.Exec(c, tx, "UPDATE InvoiceDetails SET InvoiceStatus = 'Received', CertifiedByUserId = NULL, CertifiedByName = NULL, CertifiedOn = NULL WHERE ItemId = @id", new { id });
            // the holder stops being a participant unless they have another reason to be one (R2-5)
            var uid = u.UserId();
            var keep = uid == it.InwardedByUserId || Copies(c, tx, id).Contains(uid)
                       || Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM InwardMovements WHERE ItemId = @id AND ToUserId = @uid AND Action IN ('Marked','Remarked','Transferred')", new { id, uid }) > 1;
            if (!keep) Db.Exec(c, tx, "DELETE FROM InwardParticipants WHERE ItemId = @id AND UserId = @uid", new { id, uid });
            Move(c, tx, id, u, "Returned", remarks: rem);
            notifications.Notify(c, tx, access.OperatorIds(it.OfficeId), NotificationActor.From(u), Msg(it, "Returned", "returned to the registry"));
            return it;
        });
        Touch(access.OperatorIds(it.OfficeId).Select(x => (long?)x).Append(u.UserId()).ToArray());
        Audit("Returned", it);
    }

    public void CopyTo(long id, List<long> userIds, bool confirmConfidential, ClaimsPrincipal u)
    {
        var (it, added) = InTx(db, (c, tx) =>
        {
            var it = LoadVisible(c, tx, id, u);
            RequireOpen(it);
            if (!IsHolder(u, it) && !IsOp(u, it)) throw new WorkflowException("Only the holder or the registry of the office can copy this item.");
            var added = AddCopies(c, tx, it, userIds, confirmConfidential, u);
            if (added.Count == 0) throw new WorkflowException("Choose at least one person who does not already have a copy.");
            return (it, added);
        });
        Touch(added.Append(u.UserId()).ToArray());
        Audit("Copied", it, $"{added.Count} user(s)" + (it.IsConfidential ? "; confidential, confirmed" : ""));
    }

    public void AddNote(long id, string? text, ClaimsPrincipal u)
    {
        var t = T("Note", text, 2000, true)!;
        var it = InTx(db, (c, tx) =>
        {
            var it = LoadVisible(c, tx, id, u);
            var full = access.HasFull(it, u);
            if (!full && !(IsOp(u, it) && !it.IsConfidential)) throw new WorkflowException("You cannot add notes to this item.");
            Db.Exec(c, tx, "INSERT INTO InwardNotes (ItemId, At, UserId, Name, Text) VALUES (@id, @at, @uid, @name, @t)",
                new { id, at = Db.Now(), uid = u.UserId(), name = u.DisplayName(), t });
            Db.Exec(c, tx, "UPDATE InwardItems SET UpdatedAt = @now WHERE Id = @id", new { id, now = Db.Now() });
            return it;
        });
        Touch(u.UserId());
        Audit("Note", it);
    }

    // ================================================================= edit

    /// <summary>Edit details (§3.2; R2-6, R2-15). Type, office and number never change. A movement lists the field names only.</summary>
    public void Edit(long id, InwardInput i, long rowVersion, ClaimsPrincipal u, bool confirmed)
    {
        var (it, fields) = InTx(db, (c, tx) =>
        {
            var it = LoadVisible(c, tx, id, u);
            RequireOpen(it);
            var full = access.HasFull(it, u);
            var inv = it.IsInvoice ? InvoiceOf(c, tx, id) : null;
            var accountsAmounts = inv != null && access.IsAccounts(u) && inv.SettledPaise == 0 && !InvStatus.IsFinal(inv.InvoiceStatus)
                                  && Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM InvoicePayments WHERE ItemId = @id", new { id }) == 0;
            var general = IsOp(u, it) || it.InwardedByUserId == u.UserId() || IsHolder(u, it);
            if (inv != null && general && inv.InvoiceStatus is not (InvStatus.Received or InvStatus.WithVerifier)) general = false;
            if (!general && !accountsAmounts)
                throw new WorkflowException(inv != null ? "This invoice can no longer be edited here (it is certified or with Accounts)." : "You cannot edit this item.");

            var changed = new List<string>();
            var args = new Dictionary<string, object?>();
            var set = new List<string>();
            void S(string col, object? oldV, object? newV, string label)
            {
                if (Equals(oldV, newV)) return;
                set.Add($"{col} = @{col}"); args[col] = newV; changed.Add(label);
            }
            if (general)
            {
                // a masked user (operator without full access to a confidential item) edits metadata only
                var keepContent = it.IsConfidential && !full;
                var src = keepContent ? new InwardInput
                {
                    SenderName = it.SenderName, SenderOrg = it.SenderOrg, SenderRef = it.SenderRef, SenderRefDate = it.SenderRefDate, Subject = it.Subject,
                    Summary = it.Summary, Category = it.Category, IsConfidential = it.IsConfidential, Irn = it.Irn,
                } : i;
                var merged = new InwardInput
                {
                    Mode = i.Mode, ReceivedOn = i.ReceivedOn, ReceivedTime = i.ReceivedTime, Priority = i.Priority, ActionRequired = i.ActionRequired,
                    ReplyDueOn = i.ReplyDueOn, KeptAt = i.KeptAt, Pages = i.Pages,
                    SenderName = src.SenderName, SenderOrg = src.SenderOrg, SenderRef = src.SenderRef, SenderRefDate = src.SenderRefDate, Subject = src.Subject,
                    Summary = src.Summary, Category = src.Category, IsConfidential = src.IsConfidential, Irn = src.Irn,
                };
                // the received date may stay as it was even if it is now beyond the backdate limit
                var keepReceived = string.IsNullOrWhiteSpace(i.ReceivedOn) || (Date("Received on", i.ReceivedOn) == OfficeDate(it.ReceivedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                   && string.IsNullOrWhiteSpace(i.ReceivedTime));
                var v = Validate(merged, it.ItemType, keepReceived ? it.ReceivedAt : null);
                if (string.IsNullOrWhiteSpace(i.ReplyDueOn) && v.ActionRequired != InwardAction.Information && it.ReplyDueOn != null && v.Priority == it.Priority) v.ReplyDueOn = it.ReplyDueOn;
                S("Mode", it.Mode, v.Mode, "mode"); S("ReceivedAt", it.ReceivedAt, v.ReceivedAt, "received at");
                S("SenderName", it.SenderName, v.SenderName, "sender"); S("SenderOrg", it.SenderOrg, v.SenderOrg, "sender's organisation");
                S("SenderRef", it.SenderRef, v.SenderRef, "sender's reference"); S("SenderRefDate", it.SenderRefDate, v.SenderRefDate, "reference date");
                S("Subject", it.Subject, v.Subject, "subject"); S("Summary", it.Summary, v.Summary, "summary"); S("Category", it.Category, v.Category, "category");
                S("Priority", it.Priority, v.Priority, "priority"); S("IsConfidential", it.IsConfidential, v.IsConfidential, "confidential");
                S("ActionRequired", it.ActionRequired, v.ActionRequired, "action required"); S("ReplyDueOn", it.ReplyDueOn, v.ReplyDueOn, "reply due");
                S("KeptAt", it.KeptAt, v.KeptAt, "kept at"); S("Pages", it.Pages, v.Pages, "pages"); S("Irn", it.Irn, v.Irn, "IRN");
            }
            if (inv != null)
            {
                var ni = ValidateInvoice(i.Invoice, general ? null : inv);
                var w = DuplicateCheck(c, tx, ni, id, confirmed);
                if (w.Count > 0) throw new InwardConfirmException(w, false);
                changed.AddRange(UpdateInvoice(c, tx, inv, ni));
            }
            if (changed.Count == 0) throw new WorkflowException("Nothing was changed.");
            Bump(c, tx, it, rowVersion, set.Count == 0 ? "Id = Id" : string.Join(", ", set), args);
            Move(c, tx, id, u, "Edited", remarks: "Changed: " + string.Join(", ", changed));
            Index(c, tx, id);
            return (it, changed);
        });
        Touch(u.UserId(), it.HolderUserId);
        Audit("Edited", it, string.Join(", ", fields));
    }

    // ================================================================= close, cancel, reopen

    public void Close(long id, CloseInput ci, long rowVersion, ClaimsPrincipal u)
    {
        var type = ci.ClosureType;
        if (type == null || !Closure.ByHolder.Contains(type) || type == Closure.ConvertedToENote) throw new WorkflowException("Choose how the item was closed.");
        var rem = T("Remarks", ci.Remarks, 1000, true);
        var (it, notify) = InTx(db, (c, tx) =>
        {
            var it = LoadVisible(c, tx, id, u);
            if (it.IsInvoice) throw new WorkflowException("Invoices are closed through payment, return or rejection.");
            if (!IsHolder(u, it)) throw new WorkflowException("Only the holder can close this item.");
            var args = new Dictionary<string, object?> { ["type"] = type, ["rem"] = rem, ["by"] = u.DisplayName() };
            var extra = "";
            switch (type)
            {
                case Closure.Replied:
                    var rd = Date("Reply date", ci.ReplyDate, true)!;
                    if (string.CompareOrdinal(rd, Today) > 0 || string.CompareOrdinal(rd, OfficeDate(it.ReceivedAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) < 0)
                        throw new WorkflowException("The reply date must be between the received date and today.");
                    args["rd"] = rd; args["out"] = T("Outward reference", ci.OutwardRef, 100);
                    extra = ", ReplyDate = @rd, OutwardRef = @out";
                    break;
                case Closure.Filed:
                    args["file"] = T("File number", ci.FiledFileNo, 100, true); extra = ", FiledFileNo = @file"; break;
                case Closure.Forwarded:
                    args["fwd"] = T("Forwarded to", ci.ForwardedTo, 200, true); extra = ", ForwardedTo = @fwd"; break;
                case Closure.Duplicate:
                    if (string.IsNullOrWhiteSpace(ci.DuplicateOfNumber)) throw new WorkflowException("Enter the inward number this item duplicates.");
                    var d = Db.Query<InwardItem>(c, tx, LoadSql + " WHERE i.Number = @n COLLATE NOCASE", new { n = ci.DuplicateOfNumber.Trim() }).FirstOrDefault();
                    if (d == null || d.Id == it.Id || !access.CanView(d, u)) throw new WorkflowException($"Inward number {ci.DuplicateOfNumber.Trim()} was not found.");
                    args["dup"] = d.Id; extra = ", DuplicateOfId = @dup"; break;
            }
            Bump(c, tx, it, rowVersion, "Status = 'Closed', ClosureType = @type, ClosureRemarks = @rem, ClosedAt = @now, ClosedByName = @by, LastHolderUserId = HolderUserId" + extra, args);
            Move(c, tx, id, u, "Closed", remarks: Closure.Label(type));
            var notify = Copies(c, tx, id).Append(it.InwardedByUserId).Distinct().ToList();
            notifications.Notify(c, tx, notify, NotificationActor.From(u), Msg(it, "Closed", "closed — " + Closure.Label(type).ToLowerInvariant()));
            return (it, notify);
        });
        Touch(notify.Select(x => (long?)x).Append(u.UserId()).ToArray());
        Audit("Closed", it, Closure.Label(type));
    }

    /// <summary>Converted to e-Note (E5; R2-8): creates (or reuses) the draft, then closes the item. Idempotent.</summary>
    public long ConvertToENote(long id, string? category, bool declaration, string? remarks, long rowVersion, ClaimsPrincipal u)
    {
        if (!enoteOptions.Enabled) throw new WorkflowException("e-Note is switched off on this server.");
        var rem = T("Remarks", remarks, 1000, true);
        var it = repo.Item(id, u);
        if (it == null || !access.CanView(it, u)) throw new WorkflowException(NotFound);
        if (it.IsInvoice || !IsHolder(u, it)) throw new WorkflowException("Only the holder of a letter or other item can convert it to an e-Note.");
        if (it.RowVersion != rowVersion) throw new WorkflowException(Changed);
        var noteId = db.Scalar<long?>("SELECT Id FROM ENotes WHERE SourceInwardId = @id", new { id });
        if (noteId == null)
        {
            if (!declaration) throw new WorkflowException("Accept the declaration to create the e-Note.");
            var subject = it.IsConfidential ? $"Inward {it.Number}" : Cut($"Inward {it.Number}: {it.Subject}", 300);
            var body = it.IsConfidential || string.IsNullOrWhiteSpace(it.Summary) ? $"See inward {it.Number}." : it.Summary!;
            try { noteId = enotes.Create(new ENoteWorkflow.DraftInput(subject, category, body, it.IsConfidential, true), u, id); }
            catch (SqliteException ex) when (ex.SqliteErrorCode == 19)          // a parallel conversion created it first
            {
                noteId = db.Scalar<long?>("SELECT Id FROM ENotes WHERE SourceInwardId = @id", new { id });
                if (noteId == null) throw;
            }
        }
        var notify = InTx(db, (c, tx) =>
        {
            var cur = LoadVisible(c, tx, id, u);
            if (!IsHolder(u, cur)) throw new WorkflowException("Only the holder can convert this item.");
            Bump(c, tx, cur, rowVersion, "Status = 'Closed', ClosureType = 'ConvertedToENote', ClosureRemarks = @rem, ENoteId = @note, ClosedAt = @now, ClosedByName = @by, LastHolderUserId = HolderUserId",
                new() { ["rem"] = rem, ["note"] = noteId, ["by"] = u.DisplayName() });
            Move(c, tx, id, u, "Closed", remarks: $"Converted to e-Note draft #{noteId}");
            var n = Copies(c, tx, id).Append(cur.InwardedByUserId).Distinct().ToList();
            notifications.Notify(c, tx, n, NotificationActor.From(u), Msg(cur, "Closed", "converted to an e-Note"));
            return n;
        });
        Touch(notify.Select(x => (long?)x).Append(u.UserId()).ToArray());
        Audit("ConvertedToENote", it, $"e-Note #{noteId}");
        return noteId!.Value;
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    /// <summary>Cancel — entered in error (§3.2, §3.3). An invoice can be cancelled only while it has no payments.</summary>
    public void Cancel(long id, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        var rem = T("Reason", reason, 1000, true);
        var (it, notify) = InTx(db, (c, tx) =>
        {
            var it = LoadVisible(c, tx, id, u);
            RequireOpen(it);
            if (!IsOp(u, it) && it.InwardedByUserId != u.UserId()) throw new WorkflowException("Only the registry of the office or the person who inwarded it can cancel this item.");
            if (it.IsInvoice)
            {
                if (Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM InvoicePayments WHERE ItemId = @id", new { id }) > 0)
                    throw new WorkflowException("This invoice has payments, so it cannot be cancelled.");
                Db.Exec(c, tx, "UPDATE InvoiceDetails SET InvoiceStatus = 'Cancelled', HoldFromStatus = NULL, StatusReason = @rem WHERE ItemId = @id", new { id, rem });
            }
            Bump(c, tx, it, rowVersion, "Status = 'Closed', ClosureType = 'Cancelled', ClosureRemarks = @rem, ClosedAt = @now, ClosedByName = @by, LastHolderUserId = COALESCE(HolderUserId, LastHolderUserId)",
                new() { ["rem"] = rem, ["by"] = u.DisplayName() });
            Move(c, tx, id, u, "Cancelled", remarks: rem);
            var notify = Copies(c, tx, id).Append(it.InwardedByUserId).Concat(it.HolderUserId is long h ? new[] { h } : Array.Empty<long>()).Distinct().ToList();
            notifications.Notify(c, tx, notify, NotificationActor.From(u), Msg(it, "Cancelled", "cancelled (entered in error)"));
            return (it, notify);
        });
        Touch(notify.Select(x => (long?)x).Append(u.UserId()).ToArray());
        Audit("Cancelled", it);
    }

    public void Reopen(long id, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        var rem = T("Reason", reason, 1000, true);
        var (it, to) = InTx(db, (c, tx) =>
        {
            var it = LoadVisible(c, tx, id, u);
            if (it.IsOpen) throw new WorkflowException("This item is already open.");
            if (it.IsInvoice) throw new WorkflowException("A closed invoice cannot be reopened; reverse a payment, or inward it again.");
            if (it.ClosureType is Closure.Cancelled or Closure.Reminder or Closure.ConvertedToENote)
                throw new WorkflowException($"An item closed as \"{Closure.Label(it.ClosureType)}\" cannot be reopened.");
            if (it.LastHolderUserId != u.UserId() && !IsOp(u, it) && !access.IsViewer(u)) throw new WorkflowException("Only the last holder, the registry of the office or an Inward Viewer can reopen this item.");
            long? to = it.LastHolderUserId is long lh && Db.Scalar<long>(c, tx, "SELECT IsActive FROM Users WHERE Id = @lh", new { lh }) == 1 ? lh : null;
            Bump(c, tx, it, rowVersion, "Status = @st, HolderUserId = @to, HolderSince = CASE WHEN @to IS NULL THEN NULL ELSE @now END, AcceptedAt = NULL, " +
                 "ClosureType = NULL, ClosureRemarks = NULL, ReplyDate = NULL, OutwardRef = NULL, FiledFileNo = NULL, ForwardedTo = NULL, DuplicateOfId = NULL, ClosedAt = NULL, ClosedByName = NULL",
                new() { ["st"] = to == null ? ItemStatus.Unassigned : ItemStatus.WithHolder, ["to"] = to });
            Move(c, tx, id, u, "Reopened", to, to is long t ? UserName(c, tx, t) : null, rem);
            var n = Copies(c, tx, id).Append(it.InwardedByUserId).Concat(to is long t2 ? new[] { t2 } : Array.Empty<long>()).Distinct().ToList();
            if (to == null) n.AddRange(access.OperatorIds(it.OfficeId));
            notifications.Notify(c, tx, n, NotificationActor.From(u), Msg(it, "Reopened", "reopened"));
            return (it, to);
        });
        Touch(to, u.UserId(), it.InwardedByUserId);
        Audit("Reopened", it);
    }

    // ================================================================= transfer

    private const string TransferWhere = "i.HolderUserId = @from AND i.Status = 'WithHolder'";

    public TransferPreview PreviewTransfer(long fromUserId, long toUserId) => db.Query<TransferPreview>($"""
        SELECT COUNT(*) AS Items, COALESCE(SUM(i.ItemType = 'Invoice'), 0) AS Invoices,
               COALESCE(SUM(i.ItemType = 'Invoice' AND i.InwardedByUserId = @to), 0) AS SkippedInvoices
        FROM InwardItems i WHERE {TransferWhere}
        """, new { from = fromUserId, to = toUserId }).First();

    /// <summary>Moves every open item held by A to B (§6.12; R2-10). One movement per item, one audit entry.</summary>
    public int Transfer(long fromUserId, long toUserId, string? remarks, ClaimsPrincipal u)
    {
        if (!access.IsViewer(u) && !u.IsAdmin()) throw new WorkflowException("Only an Inward Viewer or the Administrator can transfer items.");
        if (fromUserId == toUserId) throw new WorkflowException("Choose two different users.");
        var rem = T("Remarks", remarks, 1000);
        var n = InTx(db, (c, tx) =>
        {
            var toName = ActiveUser(c, tx, toUserId, "the user to transfer to");
            var items = Db.Query<InwardItem>(c, tx, LoadSql + $" WHERE {TransferWhere} AND NOT (i.ItemType = 'Invoice' AND i.InwardedByUserId = @to)", new { from = fromUserId, to = toUserId });
            if (items.Count == 0) throw new WorkflowException("There are no items to transfer.");
            foreach (var it in items)
            {
                Bump(c, tx, it, it.RowVersion, "HolderUserId = @to, HolderSince = @now, AcceptedAt = NULL, LastHolderUserId = @to", new() { ["to"] = toUserId });
                if (it.IsInvoice)
                    Db.Exec(c, tx, """
                        UPDATE InvoiceDetails SET CertifiedByUserId = NULL, CertifiedByName = NULL, CertifiedOn = NULL,
                               InvoiceStatus = CASE WHEN InvoiceStatus = 'Certified' THEN 'WithVerifier' ELSE InvoiceStatus END,
                               HoldFromStatus = CASE WHEN HoldFromStatus = 'Certified' THEN 'WithVerifier' ELSE HoldFromStatus END
                        WHERE ItemId = @Id
                        """, new { it.Id });
                AddParticipant(c, tx, it.Id, toUserId);
                Move(c, tx, it.Id, u, "Transferred", toUserId, toName, rem);
            }
            notifications.Notify(c, tx, new[] { toUserId }, NotificationActor.From(u), new NotificationMessage("Inward.Transferred",
                $"{items.Count} inward item(s) transferred to you", null, "/Inward/Mine", null, null));
            return items.Count;
        });
        Touch(fromUserId, toUserId, u.UserId());
        audit.Log("Inward.Transferred", "User", fromUserId, $"{n} open item(s) from user #{fromUserId} to user #{toUserId}");
        return n;
    }

    // ================================================================= access grants (E3)

    public void Grant(long userId, string role, long officeId, ClaimsPrincipal u)
    {
        if (!u.IsAdmin()) throw new WorkflowException("Only the Administrator can change inward access.");
        if (userId == u.UserId()) throw new WorkflowException("You cannot grant inward access to yourself; ask another Administrator.");
        if (!InwardRole.Labels.ContainsKey(role)) throw new WorkflowException("Choose a role.");
        if (role == InwardRole.Operator) { if (repo.Office(officeId) is not { IsActive: true }) throw new WorkflowException("Choose the office for the Inward Operator."); }
        else officeId = 0;
        InTx(db, (c, tx) =>
        {
            var name = ActiveUser(c, tx, userId, "a user");
            if (Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM InwardMembers WHERE UserId = @userId AND Role = @role AND OfficeId = @officeId", new { userId, role, officeId }) > 0)
                throw new WorkflowException($"{name} already has this access.");
            var now = Db.Now();
            Db.Exec(c, tx, "INSERT INTO InwardMembers (UserId, Role, OfficeId, GrantedByName, GrantedAt) VALUES (@userId, @role, @officeId, @by, @now)",
                new { userId, role, officeId, by = u.DisplayName(), now });
            Db.Exec(c, tx, "INSERT INTO InwardMemberHistory (UserId, Action, Role, OfficeId, ActorName, At) VALUES (@userId, 'Granted', @role, @officeId, @by, @now)",
                new { userId, role, officeId, by = u.DisplayName(), now });
            var msg = new NotificationMessage("Inward.Access", $"Inward access granted: {InwardRole.Labels[role]}", null, "/Inward", null, null);
            notifications.Notify(c, tx, access.ViewerIds().Append(userId).Distinct(), NotificationActor.From(u), msg);
            return 0;
        });
        access.Invalidate();
        Touch(new long?[] { userId }.Concat(access.ViewerIds().Select(x => (long?)x)).ToArray());
        audit.Log("Inward.AccessGranted", "User", userId, $"{role}" + (officeId > 0 ? $" for office #{officeId}" : ""));
    }

    public void Revoke(long memberId, ClaimsPrincipal u)
    {
        if (!u.IsAdmin()) throw new WorkflowException("Only the Administrator can change inward access.");
        var m = db.Single<InwardMember>("SELECT * FROM InwardMembers WHERE Id = @memberId", new { memberId }) ?? throw new WorkflowException("Access entry not found.");
        if (m.UserId == u.UserId()) throw new WorkflowException("You cannot change your own inward access; ask another Administrator.");
        InTx(db, (c, tx) =>
        {
            Db.Exec(c, tx, "DELETE FROM InwardMembers WHERE Id = @memberId", new { memberId });
            Db.Exec(c, tx, "INSERT INTO InwardMemberHistory (UserId, Action, Role, OfficeId, ActorName, At) VALUES (@UserId, 'Revoked', @Role, @OfficeId, @by, @now)",
                new { m.UserId, m.Role, m.OfficeId, by = u.DisplayName(), now = Db.Now() });
            var msg = new NotificationMessage("Inward.Access", $"Inward access removed: {InwardRole.Labels.GetValueOrDefault(m.Role, m.Role)}", null, "/Inward/Mine", null, null);
            notifications.Notify(c, tx, access.ViewerIds().Append(m.UserId).Distinct(), NotificationActor.From(u), msg);
            return 0;
        });
        access.Invalidate();
        Touch(m.UserId);
        audit.Log("Inward.AccessRevoked", "User", m.UserId, $"{m.Role}" + (m.OfficeId > 0 ? $" for office #{m.OfficeId}" : ""));
    }

    // ================================================================= daily reminders (§8)

    /// <summary>Runs the daily reminders once per office day, at or after ReminderHour (called by InwardScheduler).</summary>
    public List<string>? RunDailyIfDue()
    {
        if (!o.Enabled || fmt.LocalNow.Hour < o.ReminderHour) return null;
        var today = Today;
        if (db.Scalar<string>("SELECT Value FROM InwardState WHERE Key = 'LastReminderRun'") == today) return null;
        var result = DailyReminders(fmt.Today);
        db.Exec("INSERT INTO InwardState (Key, Value) VALUES ('LastReminderRun', @today) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value", new { today });
        return result;
    }

    private sealed class DueRow
    {
        public long Id { get; set; }
        public long HolderUserId { get; set; }
        public string ItemType { get; set; } = "";
        public string? ReplyDueOn { get; set; }
        public string? AcceptedAt { get; set; }
        public string? HolderSince { get; set; }
        public string? LastDueReminderOn { get; set; }
        public string? LastOverdueReminderOn { get; set; }
    }

    /// <summary>
    /// One summary per holder (overdue — repeating, due today or tomorrow, not accepted, invoices held too long), a digest
    /// per operator (unassigned beyond the working-day limit) and one to Accounts (MSME and duplicate overrides).
    /// Markers never bump RowVersion or UpdatedAt (R2-18). Returns counts only.
    /// </summary>
    public List<string> DailyReminders(DateOnly today)
    {
        if (!o.Enabled) return new();
        string D(int add) => today.AddDays(add).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var t = D(0);
        var notAcceptedCut = repo.DayStartUtc(today.AddDays(-o.NotAcceptedAlertDays + 1));
        var invoiceCut = repo.DayStartUtc(today.AddDays(-o.InvoiceHolderAlertDays + 1));
        var rows = db.Query<DueRow>("""
            SELECT i.Id, i.HolderUserId, i.ItemType, i.ReplyDueOn, i.AcceptedAt, i.HolderSince, i.LastDueReminderOn, i.LastOverdueReminderOn
            FROM InwardItems i JOIN Users u ON u.Id = i.HolderUserId
            WHERE i.Status = 'WithHolder' AND u.IsActive = 1
              AND ((i.ReplyDueOn IS NOT NULL AND i.ReplyDueOn <= @soon) OR (i.AcceptedAt IS NULL AND i.HolderSince < @na) OR (i.ItemType = 'Invoice' AND i.HolderSince < @ic))
            """, new { soon = D(1), na = notAcceptedCut, ic = invoiceCut });
        var per = new Dictionary<long, int[]>();          // overdue, due soon, not accepted, invoices held
        foreach (var batch in rows.Chunk(200))
            InTx(db, (c, tx) =>
            {
                foreach (var r in batch)
                {
                    var a = per.TryGetValue(r.HolderUserId, out var x) ? x : per[r.HolderUserId] = new int[4];
                    if (r.ReplyDueOn != null && string.CompareOrdinal(r.ReplyDueOn, t) < 0)
                    {
                        if (r.LastOverdueReminderOn == null || string.CompareOrdinal(r.LastOverdueReminderOn, D(-o.OverdueRepeatDays)) <= 0)
                        { a[0]++; Db.Exec(c, tx, "UPDATE InwardItems SET LastOverdueReminderOn = @t WHERE Id = @Id", new { t, r.Id }); }
                    }
                    else if (r.ReplyDueOn != null && string.CompareOrdinal(r.ReplyDueOn, D(1)) <= 0 && r.LastDueReminderOn != r.ReplyDueOn)
                    { a[1]++; Db.Exec(c, tx, "UPDATE InwardItems SET LastDueReminderOn = ReplyDueOn WHERE Id = @Id", new { r.Id }); }
                    if (r.AcceptedAt == null && string.CompareOrdinal(r.HolderSince, notAcceptedCut) < 0) a[2]++;
                    if (r.ItemType == InwardType.Invoice && string.CompareOrdinal(r.HolderSince, invoiceCut) < 0) a[3]++;
                }
                return 0;
            });
        int sent = 0, ops = 0, acc = 0;
        InTx(db, (c, tx) =>
        {
            foreach (var (uid, a) in per.Where(p => p.Value.Any(v => v > 0)))
            {
                var parts = new List<string>();
                if (a[0] > 0) parts.Add($"{a[0]} overdue");
                if (a[1] > 0) parts.Add($"{a[1]} due today or tomorrow");
                if (a[2] > 0) parts.Add($"{a[2]} not yet accepted");
                if (a[3] > 0) parts.Add($"{a[3]} invoice(s) with you for more than {o.InvoiceHolderAlertDays} days");
                sent += notifications.Notify(c, tx, new[] { uid }, SystemActor, new NotificationMessage("Inward.Reminder",
                    "Inward reminder: " + string.Join(", ", parts), "Items marked to you need attention.", "/Inward/Mine"));
            }
            var cut = repo.DayStartUtc(InwardRepository.WorkingDaysBefore(today, Math.Max(1, o.UnassignedAlertWorkingDays)).AddDays(1));
            foreach (var office in db.Query<OfficeCount>("SELECT OfficeId, COUNT(*) AS N FROM InwardItems WHERE Status = 'Unassigned' AND InwardedAt < @cut GROUP BY OfficeId", new { cut }))
                ops += notifications.Notify(c, tx, access.OperatorIds(office.OfficeId), SystemActor, new NotificationMessage("Inward.Digest",
                    $"Inward registry: {office.N} item(s) unassigned for more than {o.UnassignedAlertWorkingDays} working day(s)", null, "/Inward/Register?Status=unassigned"));
            var soon = Db.Scalar<long>(c, tx, $"SELECT COUNT(*) FROM InvoiceDetails WHERE IsMsme = 1 AND InvoiceStatus NOT IN {InvStatus.FinalSql} AND PaymentDueOn BETWEEN @t AND @s", new { t, s = D(o.MsmeDueSoonDays) });
            var past = Db.Scalar<long>(c, tx, $"SELECT COUNT(*) FROM InvoiceDetails WHERE IsMsme = 1 AND InvoiceStatus NOT IN {InvStatus.FinalSql} AND InvoiceDate < @cut", new { cut = D(-o.MsmeMaxDays) });
            var over = Db.Scalar<long>(c, tx, $"SELECT COUNT(*) FROM InvoiceDetails WHERE InvoiceStatus NOT IN {InvStatus.FinalSql} AND OverrideReason IS NOT NULL AND DuplicateCheckedBy IS NULL");
            if (soon + past + over > 0)
                acc = notifications.Notify(c, tx, access.AccountsIds(), SystemActor, new NotificationMessage("Inward.Digest",
                    $"Invoices: {soon} MSME due within {o.MsmeDueSoonDays} days, {past} MSME past {o.MsmeMaxDays} days, {over} duplicate override(s) to check", null, "/Inward/Invoices"));
            return 0;
        });
        foreach (var uid in per.Keys) tracker.TouchUser(uid);
        tracker.Touch(ChangeTracker.InwardQueue);
        var result = new List<string>();
        if (sent + ops + acc > 0) result.Add($"Inward reminders: {sent} holder(s), {ops} operator digest(s), {acc} Accounts digest(s)");
        return result;
    }

    private sealed class OfficeCount { public long OfficeId { get; set; } public long N { get; set; } }
}
