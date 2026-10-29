using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

/// <summary>The numbers behind every badge (bell, My Desk, Approval Inbox, Administration, Business Leads, e-Notes).</summary>
public sealed record DeskCounts(long Unread, long Inbox, long RequestsReturned, long Registrations, long VerticalChanges,
    long NewLeads, long MyLeadsDue, long ENotesPending, long ENotesMine, long BoardNew = 0)
{
    public long AdminPending => Registrations + VerticalChanges;
    /// <summary>Items waiting for the user's action (the "My Desk (n)" badge).</summary>
    public long Total => Inbox + RequestsReturned + Registrations + VerticalChanges + NewLeads + MyLeadsDue + ENotesPending + ENotesMine;
    public string Token { get; init; } = "";
    public static string Badge(long n) => n > 99 ? "99+" : n.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class DeskRequestRow
{
    public long Id { get; set; }
    public string? RequestNumber { get; set; }
    public string Subject { get; set; } = "";
    public string State { get; set; } = "";
    public string SubmitterName { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public sealed class DeskAdminRow
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Detail { get; set; }
    public string At { get; set; } = "";
}

public sealed class DeskLeadRow
{
    public long Id { get; set; }
    public string Reference { get; set; } = "";
    public string Title { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string Status { get; set; } = "";
    public string? DueDate { get; set; }
    public string SubmitterName { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}

public sealed class DeskENoteRow
{
    public long Id { get; set; }
    public string? NoteNumber { get; set; }
    public string Subject { get; set; } = "";
    public string State { get; set; } = "";
    public string? Outcome { get; set; }
    public string InitiatorName { get; set; } = "";
    public string? CurrentHolderName { get; set; }
    public string? SenderName { get; set; }
    public string? HeldSince { get; set; }
    public string UpdatedAt { get; set; } = "";
    public string DisplayNumber => NoteNumber ?? $"Draft #{Id}";
}

public sealed class DeskView
{
    public bool ShowENotes { get; init; }
    public List<DeskENoteRow> ENotesPending { get; init; } = new();
    public List<DeskENoteRow> ENotesReturned { get; init; } = new();
    public List<DeskENoteRow> ENotesDrafts { get; init; } = new();
    public List<DeskENoteRow> ENotesMoving { get; init; } = new();
    public List<DeskENoteRow> ENotesToClose { get; init; } = new();
    public DeskCounts Counts { get; init; } = null!;
    public bool ShowInbox { get; init; }
    public bool ShowAdmin { get; init; }
    public bool ShowLeads { get; init; }
    public List<DeskRequestRow> Inbox { get; init; } = new();
    public List<DeskRequestRow> Returned { get; init; } = new();
    public List<DeskAdminRow> Registrations { get; init; } = new();
    public List<DeskAdminRow> VerticalChanges { get; init; } = new();
    public List<DeskLeadRow> NewLeads { get; init; } = new();
    public List<DeskLeadRow> MyLeadsDue { get; init; } = new();
    public List<Notification> Recent { get; init; } = new();
}

/// <summary>
/// The signed-in user's work queue across modules. Counts come from one indexed query and are cached in memory;
/// the cache entry is dropped as soon as a relevant write bumps the user's or the queue's change marker.
/// </summary>
public sealed class DeskService(Db db, ChangeTracker tracker, LeadAccess leads, INotificationService notifications, Fmt fmt, ENoteOptions enote, BoardOptions board)
{
    private sealed record Entry(DeskCounts Counts, string Roles, long Seq, long ComputedTicks);
    private readonly ConcurrentDictionary<long, Entry> _cache = new();
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(10);   // safety net (e.g. "due within 7 days" moves with the date)

    private IEnumerable<string> Keys(ClaimsPrincipal u)
    {
        yield return "u:" + u.UserId();
        if (u.IsAdmin()) yield return ChangeTracker.AdminQueue;
        if (leads.CanManage(u)) yield return ChangeTracker.LeadQueue;
        yield return ChangeTracker.ENoteQueue;
        yield return ChangeTracker.BoardQueue;
    }

    private string RoleKey(ClaimsPrincipal u) => $"{u.IsAdmin()}|{u.IsApprover()}|{leads.CanManage(u)}|{u.Vertical()}";

    public DeskCounts Counts(ClaimsPrincipal u)
    {
        var uid = u.UserId();
        if (uid <= 0) return new DeskCounts(0, 0, 0, 0, 0, 0, 0, 0, 0);
        var (seq, ticks) = tracker.Latest(Keys(u));
        var roles = RoleKey(u);
        var now = Environment.TickCount64;
        // A count read within 1 s of a change could have been taken just before that change was committed: re-read it next time.
        if (_cache.TryGetValue(uid, out var e) && e.Seq == seq && e.Roles == roles && e.ComputedTicks >= ticks + 1000
            && now - e.ComputedTicks < MaxAge.TotalMilliseconds)
            return e.Counts;
        var counts = Compute(u);
        _cache[uid] = new Entry(counts, roles, seq, now);
        return counts;
    }

    private sealed class CountRow
    {
        public long Unread { get; set; }
        public long Inbox { get; set; }
        public long Returned { get; set; }
        public long Registrations { get; set; }
        public long VerticalChanges { get; set; }
        public long NewLeads { get; set; }
        public long MyLeadsDue { get; set; }
        public long ENotesPending { get; set; }
        public long ENotesMine { get; set; }
        public long BoardNew { get; set; }
    }

    private DeskCounts Compute(ClaimsPrincipal u)
    {
        var r = db.Query<CountRow>($"""
            SELECT
              (SELECT COUNT(*) FROM Notifications WHERE UserId = @uid AND ReadAt IS NULL) AS Unread,
              CASE WHEN @approver = 1 THEN (SELECT COUNT(*) FROM Requests WHERE AssignedToUserId = @uid AND State IN ('{RequestStates.Submitted}', '{RequestStates.UnderReview}')) ELSE 0 END AS Inbox,
              (SELECT COUNT(*) FROM Requests WHERE SubmitterUserId = @uid AND State = '{RequestStates.Returned}') AS Returned,
              CASE WHEN @admin = 1 THEN (SELECT COUNT(*) FROM RegistrationRequests WHERE Status = 'Pending') ELSE 0 END AS Registrations,
              CASE WHEN @admin = 1 THEN (SELECT COUNT(*) FROM ProfileChangeRequests WHERE Status = 'Pending') ELSE 0 END AS VerticalChanges,
              CASE WHEN @manager = 1 THEN (SELECT COUNT(*) FROM Leads WHERE Status = '{LeadStatus.Submitted}') ELSE 0 END AS NewLeads,
              (SELECT COUNT(*) FROM Leads WHERE OwnerUserId = @uid AND Status NOT IN ({OpenExcluded}) AND DueDate IS NOT NULL AND DueDate <> '' AND DueDate <= @soon) AS MyLeadsDue,
              CASE WHEN @enote = 1 THEN (SELECT COUNT(*) FROM ENotes WHERE CurrentHolderUserId = @uid AND State IN ('{ENoteStates.InMovement}', '{ENoteStates.Returned}')) ELSE 0 END AS ENotesPending,
              CASE WHEN @enote = 1 THEN (SELECT COUNT(*) FROM ENotes WHERE CurrentHolderUserId = @uid AND InitiatorUserId = @uid AND State IN ('{ENoteStates.Approved}', '{ENoteStates.Rejected}', '{ENoteStates.Withdrawn}')) ELSE 0 END AS ENotesMine,
              CASE WHEN @board = 1 THEN (SELECT COUNT(*) FROM BoardPosts WHERE IsDeleted = 0 AND AuthorUserId <> @uid AND CreatedAt > @boardFloor
                   AND CreatedAt > COALESCE((SELECT BoardSeenAt FROM Users WHERE Id = @uid), @boardFloor)) ELSE 0 END AS BoardNew
            """, Args(u)).First();
        var c = new DeskCounts(r.Unread, r.Inbox, r.Returned, r.Registrations, r.VerticalChanges, r.NewLeads, r.MyLeadsDue, r.ENotesPending, r.ENotesMine, r.BoardNew);
        return c with { Token = Hash($"{tracker.Boot}|{u.UserId()}|{c.Unread}|{c.Total}|{c.Inbox}|{c.AdminPending}|{c.NewLeads}|{c.ENotesPending}|{c.ENotesMine}|{c.BoardNew}") };
    }

    private static readonly string OpenExcluded = string.Join(", ", LeadStatus.Closed.Select(s => "'" + s.Replace("'", "''") + "'"));

    private object Args(ClaimsPrincipal u) => new
    {
        uid = u.UserId(), approver = u.IsApprover(), admin = u.IsAdmin(), manager = leads.CanManage(u),
        soon = fmt.Today.AddDays(7).ToString("yyyy-MM-dd"), enote = enote.Enabled,
        board = board.Enabled, boardFloor = DateTime.UtcNow.AddDays(-30).ToString("o"),
    };

    private const string ENoteRowSelect = """
        SELECT n.Id, n.NoteNumber, n.Subject, n.State, n.Outcome, n.InitiatorName, n.CurrentHolderName, p.DisplayName AS SenderName, n.HeldSince, n.UpdatedAt
        FROM ENotes n LEFT JOIN Users p ON p.Id = n.PreviousHolderUserId
        """;

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..20];

    /// <summary>The My Desk page: up to 10 items per section, each section one indexed query.</summary>
    public DeskView View(ClaimsPrincipal u)
    {
        var uid = u.UserId();
        var a = Args(u);
        _cache.TryRemove(uid, out _);
        var counts = Counts(u);
        var manager = leads.CanManage(u);
        var en = enote.Enabled;
        return new DeskView
        {
            Counts = counts,
            ShowENotes = en,
            ENotesPending = en ? db.Query<DeskENoteRow>(ENoteRowSelect + $"""
                 WHERE n.CurrentHolderUserId = @uid AND n.InitiatorUserId <> @uid AND n.State = '{ENoteStates.InMovement}' ORDER BY n.HeldSince LIMIT 10
                """, a) : new(),
            ENotesReturned = en ? db.Query<DeskENoteRow>(ENoteRowSelect + $"""
                 WHERE n.CurrentHolderUserId = @uid AND n.InitiatorUserId = @uid AND n.State = '{ENoteStates.Returned}' ORDER BY n.HeldSince LIMIT 10
                """, a) : new(),
            ENotesDrafts = en ? db.Query<DeskENoteRow>(ENoteRowSelect + $"""
                 WHERE n.InitiatorUserId = @uid AND n.State = '{ENoteStates.Draft}' ORDER BY n.UpdatedAt DESC LIMIT 10
                """, a) : new(),
            ENotesMoving = en ? db.Query<DeskENoteRow>(ENoteRowSelect + $"""
                 WHERE n.InitiatorUserId = @uid AND n.State = '{ENoteStates.InMovement}' ORDER BY n.HeldSince LIMIT 10
                """, a) : new(),
            ENotesToClose = en ? db.Query<DeskENoteRow>(ENoteRowSelect + $"""
                 WHERE n.CurrentHolderUserId = @uid AND n.InitiatorUserId = @uid AND n.State IN ('{ENoteStates.Approved}', '{ENoteStates.Rejected}', '{ENoteStates.Withdrawn}')
                 ORDER BY n.UpdatedAt LIMIT 10
                """, a) : new(),
            ShowInbox = u.IsApprover() || counts.Inbox > 0,
            ShowAdmin = u.IsAdmin(),
            ShowLeads = manager,
            Inbox = u.IsApprover() ? db.Query<DeskRequestRow>($"""
                SELECT Id, RequestNumber, Subject, State, SubmitterName, UpdatedAt FROM Requests
                WHERE AssignedToUserId = @uid AND State IN ('{RequestStates.Submitted}', '{RequestStates.UnderReview}') ORDER BY UpdatedAt LIMIT 10
                """, a) : new(),
            Returned = counts.RequestsReturned > 0 ? db.Query<DeskRequestRow>($"""
                SELECT Id, RequestNumber, Subject, State, SubmitterName, UpdatedAt FROM Requests
                WHERE SubmitterUserId = @uid AND State = '{RequestStates.Returned}' ORDER BY UpdatedAt LIMIT 10
                """, a) : new(),
            Registrations = u.IsAdmin() ? db.Query<DeskAdminRow>("""
                SELECT Id, FullName AS Name, COALESCE(Designation, '') || CASE WHEN Vertical IS NULL OR Vertical = '' THEN '' ELSE ' · ' || Vertical END AS Detail, SubmittedAt AS At
                FROM RegistrationRequests WHERE Status = 'Pending' ORDER BY SubmittedAt LIMIT 10
                """) : new(),
            VerticalChanges = u.IsAdmin() ? db.Query<DeskAdminRow>("""
                SELECT r.Id, u.DisplayName AS Name, COALESCE(r.OldValue, '(none)') || ' → ' || COALESCE(r.NewValue, '(none)') AS Detail, r.RequestedAt AS At
                FROM ProfileChangeRequests r JOIN Users u ON u.Id = r.UserId WHERE r.Status = 'Pending' ORDER BY r.RequestedAt LIMIT 10
                """) : new(),
            NewLeads = manager ? db.Query<DeskLeadRow>($"""
                SELECT Id, Reference, Title, ClientName, Status, DueDate, SubmitterName, CreatedAt FROM Leads
                WHERE Status = '{LeadStatus.Submitted}' ORDER BY CreatedAt LIMIT 10
                """) : new(),
            MyLeadsDue = counts.MyLeadsDue > 0 ? db.Query<DeskLeadRow>($"""
                SELECT Id, Reference, Title, ClientName, Status, DueDate, SubmitterName, CreatedAt FROM Leads
                WHERE OwnerUserId = @uid AND Status NOT IN ({OpenExcluded}) AND DueDate IS NOT NULL AND DueDate <> '' AND DueDate <= @soon
                ORDER BY DueDate LIMIT 10
                """, a) : new(),
            Recent = notifications.Latest(uid, 5),
        };
    }
}
