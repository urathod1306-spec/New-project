using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

public static class IndianStates
{
    public static readonly string[] All =
    {
        "Andhra Pradesh", "Arunachal Pradesh", "Assam", "Bihar", "Chhattisgarh", "Goa", "Gujarat", "Haryana", "Himachal Pradesh",
        "Jharkhand", "Karnataka", "Kerala", "Madhya Pradesh", "Maharashtra", "Manipur", "Meghalaya", "Mizoram", "Nagaland", "Odisha",
        "Punjab", "Rajasthan", "Sikkim", "Tamil Nadu", "Telangana", "Tripura", "Uttar Pradesh", "Uttarakhand", "West Bengal",
        "Andaman and Nicobar Islands", "Chandigarh", "Dadra and Nagar Haveli and Daman and Diu", "Delhi", "Jammu and Kashmir",
        "Ladakh", "Lakshadweep", "Puducherry", "Multiple states / Pan-India", "Outside India",
    };
}

public static class LeadStatus
{
    public const string Submitted = "Submitted";
    public const string UnderReview = "Under Review";
    public const string Preparing = "Documents Being Prepared";
    public const string Applied = "Applied / Bid Submitted";
    public const string Won = "Won";
    public const string Lost = "Lost";
    public const string Dropped = "Dropped / Not Pursued";

    /// <summary>Pipeline order.</summary>
    public static readonly string[] All = { Submitted, UnderReview, Preparing, Applied, Won, Lost, Dropped };
    public static readonly string[] Closed = { Won, Lost, Dropped };
    public static bool IsClosed(string s) => Closed.Contains(s);
    public static bool NeedsReason(string s) => s is Lost or Dropped;
    public static string Css(string s) => s switch
    {
        Won => "success", Lost => "danger", Dropped => "neutral", Submitted => "Submitted", Applied => "info", _ => "warning",
    };
}

public sealed class Lead
{
    public long Id { get; set; }
    public string Reference { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public long Serial { get; set; }
    public string Title { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string? ClientType { get; set; }
    public string? Sector { get; set; }
    public string? StateLocation { get; set; }
    public string? Source { get; set; }
    public double? EstimatedValueLakh { get; set; }
    public string? DueDate { get; set; }
    public string? ContactName { get; set; }
    public string? ContactDesignation { get; set; }
    public string? ContactPhone { get; set; }
    public string? ContactEmail { get; set; }
    public string Description { get; set; } = "";
    public string? TenderNumber { get; set; }
    public string Status { get; set; } = LeadStatus.Submitted;
    public long? OwnerUserId { get; set; }
    public long SubmitterUserId { get; set; }
    public string SubmitterName { get; set; } = "";
    public string? SubmitterVertical { get; set; }
    public string? LossReason { get; set; }
    public string? LossReasonText { get; set; }
    public double? AwardedValueLakh { get; set; }
    public string? AwardDate { get; set; }
    public long RowVersion { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
    // joined
    public string? OwnerName { get; set; }
}

public sealed class LeadEvent
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public string EventType { get; set; } = "";
    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public string? Remarks { get; set; }
    public string? Detail { get; set; }
    public long ActorUserId { get; set; }
    public string ActorName { get; set; } = "";
    public string Timestamp { get; set; } = "";
}

public sealed class LeadAttachment
{
    public long Id { get; set; }
    public long LeadId { get; set; }
    public string Side { get; set; } = "Submitter";
    public string StoredPath { get; set; } = "";
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
    public long UploadedByUserId { get; set; }
    public string UploadedBy { get; set; } = "";
    public string UploadedAt { get; set; } = "";
}

public sealed class LeadFilter
{
    public string? Q { get; set; }
    public string? Status { get; set; }
    public string? ClientType { get; set; }
    public string? Sector { get; set; }
    public long? OwnerUserId { get; set; }
    public string? Submitter { get; set; }
    public string? FinancialYear { get; set; }
    public bool DueSoon { get; set; }
    public string Sort { get; set; } = "updated";
}

/// <summary>One place to plug in e-mail (or other) notifications later. Every lead event passes through here.</summary>
public interface ILeadNotifier
{
    void LeadEventOccurred(Lead lead, LeadEvent ev);
}

public sealed class NoLeadNotifications : ILeadNotifier
{
    public void LeadEventOccurred(Lead lead, LeadEvent ev) { }
}

/// <summary>
/// Who may do what with business leads. BDU membership = the user's current, approved vertical is the configured
/// Business Development vertical. COO/MD = the LeadViewer role. Administrators get no lead powers by themselves.
/// </summary>
public sealed class LeadAccess(IntranetOptions o, MasterRepository masters)
{
    public string BdVerticalName
    {
        get
        {
            if (o.BusinessDevelopmentVerticalId > 0 && masters.Get(o.BusinessDevelopmentVerticalId) is { Type: MasterTypes.Vertical } m) return m.Name;
            return o.BusinessDevelopmentVerticalName;
        }
    }

    public bool IsBdu(ClaimsPrincipal u) =>
        u.UserId() > 0 && !string.IsNullOrEmpty(u.Vertical()) && string.Equals(u.Vertical(), BdVerticalName, StringComparison.OrdinalIgnoreCase);

    public bool IsLeadViewer(ClaimsPrincipal u) => u.UserId() > 0 && u.IsInRole(Roles.LeadViewer);

    /// <summary>See every lead, the pipeline, dashboard and export; update status, owner, remarks, Won/Lost/Dropped.</summary>
    public bool CanManage(ClaimsPrincipal u) => IsBdu(u) || IsLeadViewer(u);

    public bool CanView(Lead l, ClaimsPrincipal u) => CanManage(u) || l.SubmitterUserId == u.UserId();

    /// <summary>The submitter may add a follow-up note or file while the lead is Submitted or Under Review.</summary>
    public bool CanSubmitterFollowUp(Lead l, ClaimsPrincipal u) =>
        l.SubmitterUserId == u.UserId() && l.Status is LeadStatus.Submitted or LeadStatus.UnderReview;
}

public sealed record LeadUpdate(string NewStatus, string Remarks, string? LossReason, string? LossReasonText, double? AwardedValueLakh, string? AwardDate);

public sealed class LeadRepository(Db db, AuditService audit, ILeadNotifier notifier, Fmt fmt, LeadAccess access)
{
    private const string Select = "SELECT l.*, o.DisplayName AS OwnerName FROM Leads l LEFT JOIN Users o ON o.Id = l.OwnerUserId";

    /// <summary>Indian financial year (April–March) containing the date, e.g. 2026-27.</summary>
    public static string FinancialYear(DateOnly d)
    {
        int start = d.Month >= 4 ? d.Year : d.Year - 1;
        return $"{start}-{(start + 1) % 100:D2}";
    }

    public static string FormatReference(string fy, long serial) => $"NCS/BD/{fy}/{serial:D4}";

    public Lead? Get(long id) => db.Single<Lead>(Select + " WHERE l.Id = @id", new { id });

    public List<Lead> Mine(long userId) => db.Query<Lead>(Select + " WHERE l.SubmitterUserId = @userId ORDER BY l.Id DESC", new { userId });

    public long CountSubmitted() => db.Scalar<long>("SELECT COUNT(*) FROM Leads WHERE Status = @s", new { s = LeadStatus.Submitted });

    public List<string> FinancialYears() => db.Strings("SELECT DISTINCT FinancialYear FROM Leads ORDER BY FinancialYear DESC");

    public List<AppUser> BduMembers() => db.Query<AppUser>(
        "SELECT * FROM Users WHERE IsActive = 1 AND Vertical = @v COLLATE NOCASE ORDER BY DisplayName", new { v = access.BdVerticalName });

    public List<LeadEvent> Timeline(long leadId) => db.Query<LeadEvent>("SELECT * FROM LeadEvents WHERE LeadId = @leadId ORDER BY Id", new { leadId });

    public List<LeadAttachment> Attachments(long leadId) => db.Query<LeadAttachment>("SELECT * FROM LeadAttachments WHERE LeadId = @leadId ORDER BY Id", new { leadId });

    public LeadAttachment? GetAttachment(long id) => db.Single<LeadAttachment>("SELECT * FROM LeadAttachments WHERE Id = @id", new { id });

    public long AttachmentBytes(long leadId) => db.Scalar<long>("SELECT COALESCE(SUM(SizeBytes), 0) FROM LeadAttachments WHERE LeadId = @leadId", new { leadId });

    /// <summary>Leads in the last 180 days with the same tender number, or the same client and a similar title.</summary>
    public List<Lead> PossibleDuplicates(string client, string title, string? tender)
    {
        var since = DateTime.UtcNow.AddDays(-180).ToString("o");
        var recent = db.Query<Lead>(Select + " WHERE l.CreatedAt >= @since", new { since });
        var c = Norm(client);
        var t = Tokens(title);
        var tn = Norm(tender);
        return recent.Where(l =>
            (tn.Length > 0 && Norm(l.TenderNumber) == tn) ||
            (Norm(l.ClientName) == c && Similar(t, Tokens(l.Title)))).Take(5).ToList();
    }

    private static string Norm(string? s) => Regex.Replace((s ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    private static HashSet<string> Tokens(string? s) => Norm(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).ToHashSet();
    private static bool Similar(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return false;
        double inter = a.Intersect(b).Count(), union = a.Union(b).Count();
        return inter / union >= 0.5 || (inter >= 2 && (a.IsSubsetOf(b) || b.IsSubsetOf(a)));
    }

    /// <summary>
    /// Creates a lead with the next reference for the financial year. The serial is taken with an atomic
    /// UPSERT inside a write transaction, so simultaneous submissions can never get the same number.
    /// </summary>
    public Lead Create(Lead l, ClaimsPrincipal user, DateOnly? today = null)
    {
        var fy = FinancialYear(today ?? fmt.Today);
        var now = Db.Now();
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var serial = Db.Scalar<long>(c, tx, """
            INSERT INTO LeadSerials (FinancialYear, LastSerial) VALUES (@fy, 1)
            ON CONFLICT(FinancialYear) DO UPDATE SET LastSerial = LastSerial + 1
            RETURNING LastSerial
            """, new { fy });
        l.FinancialYear = fy;
        l.Serial = serial;
        l.Reference = FormatReference(fy, serial);
        l.Status = LeadStatus.Submitted;
        l.SubmitterUserId = user.UserId();
        l.SubmitterName = user.DisplayName();
        l.SubmitterVertical = user.Vertical();
        l.CreatedAt = now;
        l.UpdatedAt = now;
        l.Id = Db.Insert(c, tx, """
            INSERT INTO Leads (Reference, FinancialYear, Serial, Title, ClientName, ClientType, Sector, StateLocation, Source, EstimatedValueLakh,
                DueDate, ContactName, ContactDesignation, ContactPhone, ContactEmail, Description, TenderNumber, Status, SubmitterUserId,
                SubmitterName, SubmitterVertical, RowVersion, CreatedAt, UpdatedAt)
            VALUES (@Reference, @FinancialYear, @Serial, @Title, @ClientName, @ClientType, @Sector, @StateLocation, @Source, @EstimatedValueLakh,
                @DueDate, @ContactName, @ContactDesignation, @ContactPhone, @ContactEmail, @Description, @TenderNumber, @Status, @SubmitterUserId,
                @SubmitterName, @SubmitterVertical, 1, @CreatedAt, @UpdatedAt)
            """, l);
        var ev = AddEvent(c, tx, l.Id, "Submitted", null, LeadStatus.Submitted, null, null, user, now);
        tx.Commit();
        audit.Log("Lead.Submitted", "Lead", l.Id, $"{l.Reference}: {l.Title} ({l.ClientName})");
        notifier.LeadEventOccurred(l, ev);
        return l;
    }

    public void AddAttachment(Lead l, StoredFile f, string side, ClaimsPrincipal user)
    {
        var now = Db.Now();
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        Db.Insert(c, tx, """
            INSERT INTO LeadAttachments (LeadId, Side, StoredPath, OriginalFileName, ContentType, SizeBytes, Sha256, UploadedByUserId, UploadedBy, UploadedAt)
            VALUES (@LeadId, @side, @StoredPath, @OriginalFileName, @ContentType, @SizeBytes, @Sha256, @uid, @name, @now)
            """, new { LeadId = l.Id, side, f.StoredPath, f.OriginalFileName, f.ContentType, f.SizeBytes, f.Sha256, uid = user.UserId(), name = user.DisplayName(), now });
        var ev = AddEvent(c, tx, l.Id, "Attachment", null, null, null, $"{f.OriginalFileName} ({Fmt.Size(f.SizeBytes)}, {side})", user, now);
        Db.Exec(c, tx, "UPDATE Leads SET UpdatedAt = @now WHERE Id = @Id", new { now, l.Id });
        tx.Commit();
        audit.Log("Lead.AttachmentAdded", "Lead", l.Id, $"{l.Reference}: {f.OriginalFileName} ({f.SizeBytes} bytes, SHA-256 {f.Sha256})");
        notifier.LeadEventOccurred(l, ev);
    }

    public void AddNote(Lead l, string note, ClaimsPrincipal user, string type = "Note")
    {
        var now = Db.Now();
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var ev = AddEvent(c, tx, l.Id, type, null, null, note, null, user, now);
        Db.Exec(c, tx, "UPDATE Leads SET UpdatedAt = @now WHERE Id = @Id", new { now, l.Id });
        tx.Commit();
        audit.Log("Lead." + type, "Lead", l.Id, $"{l.Reference}: {note}");
        notifier.LeadEventOccurred(l, ev);
    }

    public void ChangeStatus(Lead l, LeadUpdate u, long expectedVersion, ClaimsPrincipal user)
    {
        if (!LeadStatus.All.Contains(u.NewStatus)) throw new WorkflowException("Choose a valid status.");
        if (string.IsNullOrWhiteSpace(u.Remarks)) throw new WorkflowException("A remark is required for every status change.");
        if (u.NewStatus == l.Status) throw new WorkflowException("The lead is already in that status.");
        string? reason = null, reasonText = null, awardDate = null;
        double? award = null;
        if (LeadStatus.NeedsReason(u.NewStatus))
        {
            if (string.IsNullOrWhiteSpace(u.LossReason)) throw new WorkflowException($"Choose a reason for marking the lead “{u.NewStatus}”.");
            reason = u.LossReason.Trim();
            reasonText = string.IsNullOrWhiteSpace(u.LossReasonText) ? null : u.LossReasonText.Trim();
            if (reason.Equals("Other", StringComparison.OrdinalIgnoreCase) && reasonText == null) throw new WorkflowException("Describe the reason when choosing “Other”.");
        }
        if (u.NewStatus == LeadStatus.Won)
        {
            if (u.AwardedValueLakh is not > 0) throw new WorkflowException("Enter the awarded value (₹ lakh) for a won lead.");
            if (!DateOnly.TryParseExact(u.AwardDate ?? "", "yyyy-MM-dd", out _)) throw new WorkflowException("Enter the award date for a won lead.");
            award = u.AwardedValueLakh;
            awardDate = u.AwardDate;
        }
        var now = Db.Now();
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var n = Db.Exec(c, tx, """
            UPDATE Leads SET Status = @to, LossReason = @reason, LossReasonText = @reasonText,
                   AwardedValueLakh = COALESCE(@award, AwardedValueLakh), AwardDate = COALESCE(@awardDate, AwardDate),
                   RowVersion = RowVersion + 1, UpdatedAt = @now
            WHERE Id = @Id AND RowVersion = @expectedVersion
            """, new { to = u.NewStatus, reason, reasonText, award, awardDate, now, l.Id, expectedVersion });
        if (n == 0) throw new WorkflowException("This lead was just updated by someone else. Please review the latest version and try again.");
        var detail = reason != null ? $"Reason: {reason}{(reasonText != null ? " — " + reasonText : "")}"
            : u.NewStatus == LeadStatus.Won ? $"Awarded ₹ {award!.Value.ToString("0.##", CultureInfo.InvariantCulture)} lakh on {awardDate}" : null;
        var ev = AddEvent(c, tx, l.Id, "StatusChanged", l.Status, u.NewStatus, u.Remarks.Trim(), detail, user, now);
        tx.Commit();
        audit.Log("Lead.StatusChanged", "Lead", l.Id, $"{l.Reference}: {l.Status} -> {u.NewStatus}; {u.Remarks.Trim()}" + (detail != null ? "; " + detail : ""));
        notifier.LeadEventOccurred(l, ev);
    }

    public void AssignOwner(Lead l, long? ownerUserId, long expectedVersion, ClaimsPrincipal user)
    {
        string? ownerName = null;
        if (ownerUserId != null)
        {
            var owner = BduMembers().FirstOrDefault(m => m.Id == ownerUserId) ?? throw new WorkflowException("The owner must be an active member of the BDU vertical.");
            ownerName = owner.DisplayName;
        }
        var now = Db.Now();
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var n = Db.Exec(c, tx, "UPDATE Leads SET OwnerUserId = @ownerUserId, RowVersion = RowVersion + 1, UpdatedAt = @now WHERE Id = @Id AND RowVersion = @expectedVersion",
            new { ownerUserId, now, l.Id, expectedVersion });
        if (n == 0) throw new WorkflowException("This lead was just updated by someone else. Please review the latest version and try again.");
        var ev = AddEvent(c, tx, l.Id, "OwnerChanged", null, null, null, $"{l.OwnerName ?? "unassigned"} → {ownerName ?? "unassigned"}", user, now);
        tx.Commit();
        audit.Log("Lead.OwnerChanged", "Lead", l.Id, $"{l.Reference}: owner {l.OwnerName ?? "-"} -> {ownerName ?? "-"}");
        notifier.LeadEventOccurred(l, ev);
    }

    private static LeadEvent AddEvent(Microsoft.Data.Sqlite.SqliteConnection c, Microsoft.Data.Sqlite.SqliteTransaction tx, long leadId, string type,
        string? from, string? to, string? remarks, string? detail, ClaimsPrincipal user, string now)
    {
        var ev = new LeadEvent
        {
            LeadId = leadId, EventType = type, FromStatus = from, ToStatus = to, Remarks = remarks, Detail = detail,
            ActorUserId = user.UserId(), ActorName = user.DisplayName(), Timestamp = now,
        };
        ev.Id = Db.Insert(c, tx, """
            INSERT INTO LeadEvents (LeadId, EventType, FromStatus, ToStatus, Remarks, Detail, ActorUserId, ActorName, Timestamp)
            VALUES (@LeadId, @EventType, @FromStatus, @ToStatus, @Remarks, @Detail, @ActorUserId, @ActorName, @Timestamp)
            """, ev);
        return ev;
    }

    public List<Lead> Pipeline(LeadFilter f)
    {
        var where = new List<string> { "1 = 1" };
        if (!string.IsNullOrWhiteSpace(f.Q))
            where.Add(@"(l.Title LIKE @like ESCAPE '\' OR l.ClientName LIKE @like ESCAPE '\' OR l.Reference LIKE @like ESCAPE '\'
                         OR l.TenderNumber LIKE @like ESCAPE '\' OR l.Description LIKE @like ESCAPE '\')");
        if (!string.IsNullOrWhiteSpace(f.Status)) where.Add(f.Status == "Open" ? "l.Status NOT IN ('Won', 'Lost', 'Dropped / Not Pursued')" : "l.Status = @Status");
        if (!string.IsNullOrWhiteSpace(f.ClientType)) where.Add("l.ClientType = @ClientType");
        if (!string.IsNullOrWhiteSpace(f.Sector)) where.Add("l.Sector = @Sector");
        if (f.OwnerUserId is > 0) where.Add("l.OwnerUserId = @OwnerUserId");
        if (f.OwnerUserId == -1) where.Add("l.OwnerUserId IS NULL");
        if (!string.IsNullOrWhiteSpace(f.Submitter)) where.Add(@"l.SubmitterName LIKE @sublike ESCAPE '\'");
        if (!string.IsNullOrWhiteSpace(f.FinancialYear)) where.Add("l.FinancialYear = @FinancialYear");
        if (f.DueSoon) where.Add("l.DueDate IS NOT NULL AND l.DueDate <> '' AND l.DueDate <= @soon AND l.Status NOT IN ('Won', 'Lost', 'Dropped / Not Pursued')");
        var order = f.Sort switch
        {
            "due" => "CASE WHEN l.DueDate IS NULL OR l.DueDate = '' THEN 1 ELSE 0 END, l.DueDate",
            "value" => "COALESCE(l.EstimatedValueLakh, 0) DESC",
            "ref" => "l.FinancialYear DESC, l.Serial DESC",
            "client" => "l.ClientName COLLATE NOCASE",
            "status" => "CASE l.Status " + string.Join(" ", LeadStatus.All.Select((s, i) => $"WHEN '{s.Replace("'", "''")}' THEN {i}")) + " END",
            _ => "l.UpdatedAt DESC",
        };
        return db.Query<Lead>(Select + " WHERE " + string.Join(" AND ", where) + " ORDER BY " + order + " LIMIT 2000", new
        {
            like = SearchText.Like(f.Q), f.Status, f.ClientType, f.Sector, f.OwnerUserId, sublike = SearchText.Like(f.Submitter), f.FinancialYear,
            soon = fmt.Today.AddDays(7).ToString("yyyy-MM-dd"),
        });
    }

    public List<Lead> All(string? fy) => db.Query<Lead>(Select + (string.IsNullOrEmpty(fy) ? "" : " WHERE l.FinancialYear = @fy"), new { fy });
}
