using System.Globalization;
using System.Security.Claims;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

/// <summary>Filters of the Points list (DD-07 §6).</summary>
public sealed class CompliancePointFilter
{
    public string? Source { get; set; }
    public long? ForumId { get; set; }
    public long? EventId { get; set; }
    /// <summary>open (default), overdue, duesoon, due30, awaiting, revision, proposed, completed, dropped, merged, all.</summary>
    public string? Status { get; set; }
    public string? Vertical { get; set; }
    public long? OfficerId { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string? Q { get; set; }
    public int Page { get; set; } = 1;

    public static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["open"] = "Open (all)", ["overdue"] = "Overdue", ["duesoon"] = "Due soon", ["due30"] = "Due within 30 days",
        ["ongoing"] = "Ongoing", ["external"] = "Guidance awaited (external)",
        ["awaiting"] = "Update awaiting review", ["revision"] = "Revision requested", ["proposed"] = "Proposed for closure",
        ["completed"] = "Completed", ["dropped"] = "Dropped", ["merged"] = "Merged", ["all"] = "All",
    };
}

public sealed class CountRow { public string Key { get; set; } = ""; public string? Key2 { get; set; } public long N { get; set; } public long N2 { get; set; } }

public sealed class ComplianceDashboard
{
    public long Open { get; set; }
    public long Overdue { get; set; }
    public long DueSoon { get; set; }
    public long Due30 { get; set; }
    public long Awaiting { get; set; }
    public long Revisions { get; set; }
    public long Proposed { get; set; }
    public long Inactive { get; set; }
    public List<CountRow> Matrix { get; set; } = new();       // Key = source type, Key2 = status
    public List<CountRow> Ageing { get; set; } = new();       // Key = bucket
    public List<CountRow> OverdueAgeing { get; set; } = new();
    public List<CountRow> ByVertical { get; set; } = new();   // N = open, N2 = overdue
    public List<CountRow> ByOfficer { get; set; } = new();    // Key = officer name, Key2 = user id
    public List<CountRow> ClosedByMonth { get; set; } = new(); // Key = yyyy-MM
    public int ActiveAdmins { get; set; }
}

public sealed class ComplianceQueueRow
{
    public long PointId { get; set; }
    public string Number { get; set; } = "";
    public string? RefNo { get; set; }
    public string SourceType { get; set; } = "";
    public string Forum { get; set; } = "";
    public string OfficerName { get; set; } = "";
    public string? Who { get; set; }
    public string? At { get; set; }
    public string? Detail { get; set; }
    public string CurrentDueDate { get; set; } = "";
    public bool Own { get; set; }
}

/// <summary>Read-only queries of the compliance module. Every query applies the access predicate of <see cref="ComplianceAccess"/>.</summary>
public sealed class ComplianceRepository(Db db, ComplianceAccess access, ComplianceOptions o, Fmt fmt)
{
    public const int PageSize = 50;
    public const int MaxExport = 10_000;

    public const string PointSelect = """
        SELECT p.*, e.SourceType, e.ForumId, e.Forum, e.EventNo, e.Title AS EventTitle, e.HeldOn, e.IsConfidential,
               e.ReportingCycle, e.NextReportingOn, u.DisplayName AS OfficerName, u.IsActive AS OfficerActive
        FROM CompliancePoints p JOIN ComplianceEvents e ON e.Id = p.EventId JOIN Users u ON u.Id = p.PrimaryOfficerUserId
        """;

    private string Today => fmt.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string In(int days) => fmt.Today.AddDays(days).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- single records

    public ComplianceEvent? Event(long id) => db.Single<ComplianceEvent>("SELECT * FROM ComplianceEvents WHERE Id = @id", new { id });
    public CompliancePoint? Point(long id) => db.Query<CompliancePoint>(PointSelect + " WHERE p.Id = @id", new { id }).FirstOrDefault();
    public CompliancePoint? PointByNumber(string number) =>
        db.Query<CompliancePoint>(PointSelect + " WHERE p.Number = @number COLLATE NOCASE", new { number = number.Trim() }).FirstOrDefault();

    public List<ComplianceUpdate> Updates(long pointId) => db.Query<ComplianceUpdate>("SELECT * FROM ComplianceUpdates WHERE PointId = @pointId ORDER BY Id DESC", new { pointId });
    public ComplianceUpdate? Update(long id) => db.Single<ComplianceUpdate>("SELECT * FROM ComplianceUpdates WHERE Id = @id", new { id });
    public List<ComplianceRevision> Revisions(long pointId) => db.Query<ComplianceRevision>("SELECT * FROM ComplianceRevisions WHERE PointId = @pointId ORDER BY Id DESC", new { pointId });
    public ComplianceRevision? Revision(long id) => db.Single<ComplianceRevision>("SELECT * FROM ComplianceRevisions WHERE Id = @id", new { id });
    public List<ComplianceHistoryEntry> History(long pointId) => db.Query<ComplianceHistoryEntry>("SELECT * FROM ComplianceHistory WHERE PointId = @pointId ORDER BY Id", new { pointId });
    public List<PersonOption> SupportOfficers(long pointId) => db.Query<PersonOption>(
        "SELECT u.Id, u.DisplayName, u.Username FROM ComplianceSupportOfficers s JOIN Users u ON u.Id = s.UserId WHERE s.PointId = @pointId ORDER BY u.DisplayName", new { pointId });

    public List<PersonOption> ActiveUsers() => db.Query<PersonOption>("SELECT Id, DisplayName, Username FROM Users WHERE IsActive = 1 ORDER BY DisplayName");

    // ---------------------------------------------------------------- events

    public List<ComplianceEvent> Events(ClaimsPrincipal u, string? source, long? forumId, string? year)
    {
        var where = new List<string> { access.EventVisibleSql(u) };
        if (ComplianceSource.IsValid(source)) where.Add("e.SourceType = @source");
        if (forumId > 0) where.Add("e.ForumId = @forumId");
        if (!string.IsNullOrEmpty(year) && year.Length == 4 && year.All(char.IsDigit)) where.Add("substr(COALESCE(e.HeldOn, e.PeriodTo, e.ReportReceivedOn, e.CreatedAt), 1, 4) = @year");
        // officers count only their own points
        var countVis = access.SeesAll(u) ? access.PointVisibleSql(u, "p", "e") : ComplianceAccess.OfficerSql("p");
        return db.Query<ComplianceEvent>($"""
            SELECT e.*,
              (SELECT COUNT(*) FROM CompliancePoints p WHERE p.EventId = e.Id AND p.Status IN {CStatus.OpenSql} AND {countVis}) AS OpenCount,
              (SELECT COUNT(*) FROM CompliancePoints p WHERE p.EventId = e.Id AND {countVis}) AS TotalCount
            FROM ComplianceEvents e WHERE {string.Join(" AND ", where)}
            ORDER BY COALESCE(e.HeldOn, e.PeriodTo, e.ReportReceivedOn, e.CreatedAt) DESC, e.Id DESC LIMIT 500
            """, new { uid = u.UserId(), source, forumId, year });
    }

    public bool CanViewEvent(long eventId, ClaimsPrincipal u) =>
        db.Scalar<long>($"SELECT COUNT(*) FROM ComplianceEvents e WHERE e.Id = @eventId AND {access.EventVisibleSql(u)}", new { eventId, uid = u.UserId() }) > 0;

    /// <summary>Points of one event the user may see (officers: their own only), in printing order.</summary>
    public List<CompliancePoint> EventPoints(long eventId, ClaimsPrincipal u)
    {
        var vis = access.SeesAll(u) ? access.PointVisibleSql(u) : ComplianceAccess.OfficerSql("p");
        return db.Query<CompliancePoint>(PointSelect + $" WHERE p.EventId = @eventId AND {vis} ORDER BY p.Seq, p.Id", new { eventId, uid = u.UserId() });
    }

    // ---------------------------------------------------------------- points list, export, mine

    private (string where, object args) PointWhere(ClaimsPrincipal u, CompliancePointFilter f)
    {
        var w = new List<string> { access.ManagedVisibleSql(u) };
        if (ComplianceSource.IsValid(f.Source)) w.Add("e.SourceType = @Source");
        if (f.ForumId > 0) w.Add("e.ForumId = @ForumId");
        if (f.EventId > 0) w.Add("p.EventId = @EventId");
        if (!string.IsNullOrWhiteSpace(f.Vertical)) w.Add("p.OwnerVertical = @Vertical");
        if (f.OfficerId > 0) w.Add("p.PrimaryOfficerUserId = @OfficerId");
        if (!string.IsNullOrWhiteSpace(f.Category)) w.Add("p.Category = @Category");
        if (CompliancePriority.All.Contains(f.Priority)) w.Add("p.Priority = @Priority");
        w.Add((f.Status ?? "open") switch
        {
            "overdue" => $"p.Status IN {CStatus.OpenSql} AND p.CurrentDueDate < @today",
            "duesoon" => $"p.Status IN {CStatus.OpenSql} AND p.CurrentDueDate BETWEEN @today AND @soon",
            "due30" => $"p.Status IN {CStatus.OpenSql} AND p.CurrentDueDate BETWEEN @today AND @d30",
            "awaiting" => "p.PendingUpdateId IS NOT NULL",
            "revision" => "p.PendingRevisionId IS NOT NULL",
            "proposed" => "p.Status = 'ProposedClosure'",
            "ongoing" => "p.Status = 'Ongoing'",
            "external" => "p.Status = 'PendingExternal'",
            "completed" => "p.Status = 'Completed'",
            "dropped" => "p.Status = 'Dropped'",
            "merged" => "p.Status = 'Merged'",
            "all" => "1 = 1",
            _ => $"p.Status IN {CStatus.OpenSql}",
        });
        if (!string.IsNullOrWhiteSpace(f.Q)) w.Add("(p.Number LIKE @q ESCAPE '\\' OR p.RefNo LIKE @q ESCAPE '\\' OR p.RefSubject LIKE @q ESCAPE '\\' OR p.PointText LIKE @q ESCAPE '\\')");
        var args = new
        {
            uid = u.UserId(), f.Source, f.ForumId, f.EventId, f.Vertical, f.OfficerId, f.Category, f.Priority,
            today = Today, soon = In(o.DueSoonDays), d30 = In(30), q = SearchText.Like(f.Q),
        };
        return (string.Join(" AND ", w), args);
    }

    public Paged<CompliancePoint> Points(ClaimsPrincipal u, CompliancePointFilter f)
    {
        var (where, args) = PointWhere(u, f);
        var page = Math.Max(1, f.Page);
        var total = db.Scalar<long>($"SELECT COUNT(*) FROM CompliancePoints p JOIN ComplianceEvents e ON e.Id = p.EventId WHERE {where}", args);
        var items = db.Query<CompliancePoint>(PointSelect + $" WHERE {where} ORDER BY p.CurrentDueDate, p.Id LIMIT {PageSize} OFFSET {(page - 1) * PageSize}", args);
        return new Paged<CompliancePoint> { Items = items, Total = total, Page = page, PageSize = PageSize };
    }

    /// <summary>All matching rows for the Excel export; null when there are more than <see cref="MaxExport"/>.</summary>
    public List<CompliancePoint>? ExportRows(ClaimsPrincipal u, CompliancePointFilter f)
    {
        var (where, args) = PointWhere(u, f);
        var total = db.Scalar<long>($"SELECT COUNT(*) FROM CompliancePoints p JOIN ComplianceEvents e ON e.Id = p.EventId WHERE {where}", args);
        if (total > MaxExport) return null;
        return db.Query<CompliancePoint>(PointSelect + $" WHERE {where} ORDER BY e.SourceType, e.Id, p.Seq, p.Id", args);
    }

    /// <summary>The user's own points: open ones, then those closed in the last 90 days.</summary>
    public List<CompliancePoint> Mine(long userId) => db.Query<CompliancePoint>(PointSelect + $"""
         WHERE {ComplianceAccess.OfficerSql("p")}
           AND (p.Status IN {CStatus.OpenSql} OR p.ClosedAt >= @since)
         ORDER BY CASE WHEN p.Status IN {CStatus.OpenSql} THEN 0 ELSE 1 END, p.CurrentDueDate, p.Id LIMIT 500
        """, new { uid = userId, since = DateTime.UtcNow.AddDays(-90).ToString("o") });

    // ---------------------------------------------------------------- dashboard

    private sealed class TileRow
    {
        public long Open { get; set; }
        public long Overdue { get; set; }
        public long DueSoon { get; set; }
        public long Due30 { get; set; }
        public long Awaiting { get; set; }
        public long Revisions { get; set; }
        public long Proposed { get; set; }
        public long Inactive { get; set; }
    }

    public ComplianceDashboard Dashboard(ClaimsPrincipal u)
    {
        var vis = access.ManagedVisibleSql(u);
        var a = new { uid = u.UserId(), today = Today, soon = In(o.DueSoonDays), d30 = In(30), mod = $"{fmt.OffsetMinutes:+0;-0} minutes", from = fmt.Today.AddMonths(-11).ToString("yyyy-MM", CultureInfo.InvariantCulture) };
        const string from = "FROM CompliancePoints p JOIN ComplianceEvents e ON e.Id = p.EventId JOIN Users u ON u.Id = p.PrimaryOfficerUserId";
        const string open = $"p.Status IN {CStatus.OpenSql}";
        var t = db.Query<TileRow>($"""
            SELECT COALESCE(SUM({open}), 0) AS Open,
                   COALESCE(SUM({open} AND p.CurrentDueDate < @today), 0) AS Overdue,
                   COALESCE(SUM({open} AND p.CurrentDueDate BETWEEN @today AND @soon), 0) AS DueSoon,
                   COALESCE(SUM({open} AND p.CurrentDueDate BETWEEN @today AND @d30), 0) AS Due30,
                   COALESCE(SUM(p.PendingUpdateId IS NOT NULL), 0) AS Awaiting,
                   COALESCE(SUM(p.PendingRevisionId IS NOT NULL), 0) AS Revisions,
                   COALESCE(SUM(p.Status = 'ProposedClosure'), 0) AS Proposed,
                   COALESCE(SUM({open} AND u.IsActive = 0), 0) AS Inactive
            {from} WHERE {vis}
            """, a).First();
        var d = new ComplianceDashboard
        {
            Open = t.Open, Overdue = t.Overdue, DueSoon = t.DueSoon, Due30 = t.Due30, Awaiting = t.Awaiting,
            Revisions = t.Revisions, Proposed = t.Proposed, Inactive = t.Inactive,
            ActiveAdmins = access.ActiveAdminIds().Count,
        };
        d.Matrix = db.Query<CountRow>($"SELECT e.SourceType AS Key, p.Status AS Key2, COUNT(*) AS N {from} WHERE {vis} GROUP BY e.SourceType, p.Status", a);
        d.Ageing = db.Query<CountRow>($"""
            SELECT CASE WHEN g <= 30 THEN '0–30' WHEN g <= 90 THEN '31–90' WHEN g <= 180 THEN '91–180' WHEN g <= 365 THEN '181–365' ELSE '> 365' END AS Key, COUNT(*) AS N
            FROM (SELECT julianday(@today) - julianday(p.RaisedOn) AS g {from} WHERE {vis} AND {open}) GROUP BY Key
            """, a);
        d.OverdueAgeing = db.Query<CountRow>($"""
            SELECT CASE WHEN g <= 30 THEN '1–30' WHEN g <= 90 THEN '31–90' ELSE '> 90' END AS Key, COUNT(*) AS N
            FROM (SELECT julianday(@today) - julianday(p.CurrentDueDate) AS g {from} WHERE {vis} AND {open} AND p.CurrentDueDate < @today) GROUP BY Key
            """, a);
        d.ByVertical = db.Query<CountRow>($"""
            SELECT COALESCE(NULLIF(p.OwnerVertical, ''), '(not set)') AS Key, COUNT(*) AS N, COALESCE(SUM(p.CurrentDueDate < @today), 0) AS N2
            {from} WHERE {vis} AND {open} GROUP BY Key ORDER BY N DESC LIMIT 30
            """, a);
        d.ByOfficer = db.Query<CountRow>($"""
            SELECT u.DisplayName AS Key, CAST(u.Id AS TEXT) AS Key2, COUNT(*) AS N, COALESCE(SUM(p.CurrentDueDate < @today), 0) AS N2
            {from} WHERE {vis} AND {open} GROUP BY u.Id ORDER BY N DESC, N2 DESC LIMIT 20
            """, a);
        d.ClosedByMonth = db.Query<CountRow>($"""
            SELECT strftime('%Y-%m', p.ClosedAt, @mod) AS Key, COUNT(*) AS N
            {from} WHERE {vis} AND p.Status = 'Completed' AND p.ClosedAt IS NOT NULL AND strftime('%Y-%m', p.ClosedAt, @mod) >= @from
            GROUP BY Key ORDER BY Key
            """, a);
        return d;
    }

    // ---------------------------------------------------------------- Cell queue and My Desk rows

    /// <summary>The Cell's queue (DD-07 §6 Review); rows on the user's own points are flagged "needs another CA".</summary>
    public (List<ComplianceQueueRow> updates, List<ComplianceQueueRow> revisions, List<ComplianceQueueRow> proposed, List<ComplianceQueueRow> stale) Queue(ClaimsPrincipal u, int limit = 200)
    {
        var vis = access.ManagedVisibleSql(u);
        var own = ComplianceAccess.OfficerSql("p");
        var a = new { uid = u.UserId(), limit, today = Today, stale = In(-30) };
        const string pj = "FROM CompliancePoints p JOIN ComplianceEvents e ON e.Id = p.EventId JOIN Users u ON u.Id = p.PrimaryOfficerUserId";
        var updates = db.Query<ComplianceQueueRow>($"""
            SELECT p.Id AS PointId, p.Number, p.RefNo, e.SourceType, e.Forum, u.DisplayName AS OfficerName, p.CurrentDueDate,
                   cu.SubmittedByName AS Who, cu.SubmittedAt AS At, cu.ProposedStatus AS Detail, ({own} OR cu.SubmittedByUserId = @uid) AS Own
            {pj} JOIN ComplianceUpdates cu ON cu.Id = p.PendingUpdateId WHERE {vis} ORDER BY cu.SubmittedAt LIMIT @limit
            """, a);
        var revisions = db.Query<ComplianceQueueRow>($"""
            SELECT p.Id AS PointId, p.Number, p.RefNo, e.SourceType, e.Forum, u.DisplayName AS OfficerName, p.CurrentDueDate,
                   r.RequestedByName AS Who, r.RequestedAt AS At, r.OldDate || ' → ' || r.NewDate AS Detail, {own} AS Own
            {pj} JOIN ComplianceRevisions r ON r.Id = p.PendingRevisionId WHERE {vis} ORDER BY r.RequestedAt LIMIT @limit
            """, a);
        var proposed = db.Query<ComplianceQueueRow>($"""
            SELECT p.Id AS PointId, p.Number, p.RefNo, e.SourceType, e.Forum, u.DisplayName AS OfficerName, p.CurrentDueDate,
                   p.AtrAsOn AS At, {own} AS Own
            {pj} WHERE {vis} AND p.Status = 'ProposedClosure' AND p.PendingUpdateId IS NULL ORDER BY p.UpdatedAt LIMIT @limit
            """, a);
        var stale = db.Query<ComplianceQueueRow>($"""
            SELECT p.Id AS PointId, p.Number, p.RefNo, e.SourceType, e.Forum, u.DisplayName AS OfficerName, p.CurrentDueDate,
                   p.AtrAsOn AS At, {own} AS Own
            {pj} WHERE {vis} AND p.Status IN {CStatus.OpenSql} AND p.CurrentDueDate < @today AND p.PendingUpdateId IS NULL
              AND (p.AtrAsOn IS NULL OR p.AtrAsOn < @stale) ORDER BY p.CurrentDueDate LIMIT @limit
            """, a);
        return (updates, revisions, proposed, stale);
    }

    /// <summary>My Desk: the officer's own points that need attention (same rule as <c>DeskCounts.ComplianceMine</c>).</summary>
    public List<CompliancePoint> DeskMine(long userId, int limit = 10) => db.Query<CompliancePoint>(PointSelect + $"""
         WHERE {ComplianceAccess.OfficerSql("p")} AND p.Status IN {CStatus.OpenSql}
           AND (p.CurrentDueDate <= @soon OR p.LastReturnedAt IS NOT NULL OR (p.NextUpdateDue <= @today AND p.PendingUpdateId IS NULL))
         ORDER BY p.CurrentDueDate, p.Id LIMIT @limit
        """, new { uid = userId, soon = In(o.DueSoonDays), today = Today, limit });

    /// <summary>SQL for My Desk counts (embedded in DeskService's single count query). Parameters: @uid, @cSoon, @today, @cAdmin.</summary>
    public const string DeskMineSql = $"""
        (SELECT COUNT(*) FROM CompliancePoints p WHERE p.Status IN {CStatus.OpenSql}
           AND (p.PrimaryOfficerUserId = @uid OR EXISTS (SELECT 1 FROM ComplianceSupportOfficers so WHERE so.PointId = p.Id AND so.UserId = @uid))
           AND (p.CurrentDueDate <= @cSoon OR p.LastReturnedAt IS NOT NULL OR (p.NextUpdateDue <= @today AND p.PendingUpdateId IS NULL)))
        """;

    public const string DeskCellSql = $"""
        CASE WHEN @cAdmin = 1 THEN (SELECT COUNT(*) FROM CompliancePoints p WHERE
           (p.PendingUpdateId IS NOT NULL OR p.PendingRevisionId IS NOT NULL OR (p.Status = 'ProposedClosure' AND p.PendingUpdateId IS NULL))
           AND p.PrimaryOfficerUserId <> @uid AND NOT EXISTS (SELECT 1 FROM ComplianceSupportOfficers so WHERE so.PointId = p.Id AND so.UserId = @uid)) ELSE 0 END
        """;

    public const string DeskAssignedSql = """
        (SELECT EXISTS (SELECT 1 FROM CompliancePoints p WHERE p.PrimaryOfficerUserId = @uid)
             OR EXISTS (SELECT 1 FROM ComplianceSupportOfficers so WHERE so.UserId = @uid))
        """;

    // ---------------------------------------------------------------- members

    public List<ComplianceMember> Members() => db.Query<ComplianceMember>("""
        SELECT m.*, u.DisplayName, u.Username, u.IsActive FROM ComplianceMembers m JOIN Users u ON u.Id = m.UserId ORDER BY m.Role, u.DisplayName
        """);

    public List<ComplianceMemberChange> MemberHistory(int limit = 100) => db.Query<ComplianceMemberChange>("""
        SELECT h.*, COALESCE(u.DisplayName, '#' || h.UserId) AS DisplayName FROM ComplianceMemberHistory h LEFT JOIN Users u ON u.Id = h.UserId ORDER BY h.Id DESC LIMIT @limit
        """, new { limit });
}
