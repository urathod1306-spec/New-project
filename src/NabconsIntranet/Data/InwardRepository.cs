using System.Globalization;
using System.Security.Claims;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

public sealed class InwardFilter
{
    public long? OfficeId { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Type { get; set; }
    public string? Mode { get; set; }
    /// <summary>open (default), unassigned, withholder, withaccounts, overdue, closed, all.</summary>
    public string? Status { get; set; }
    public long? HolderId { get; set; }
    public string? Priority { get; set; }
    public string? Q { get; set; }
    public int Page { get; set; } = 1;

    public static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["open"] = "Open (all)", ["unassigned"] = "Unassigned", ["withholder"] = "With a holder", ["withaccounts"] = "With Accounts",
        ["overdue"] = "Overdue", ["closed"] = "Closed", ["all"] = "All",
    };
}

public sealed class InvoiceFilter
{
    /// <summary>open (default), unpaid, msme45, msmesoon, hold, override, paid, all.</summary>
    public string? Status { get; set; }
    public string? Project { get; set; }
    public string? Q { get; set; }
    public int Page { get; set; } = 1;

    public static readonly Dictionary<string, string> StatusLabels = new()
    {
        ["open"] = "Open (not settled)", ["accounts"] = "With Accounts", ["msme45"] = "MSME past 45 days", ["msmesoon"] = "MSME due soon",
        ["hold"] = "On hold", ["override"] = "Duplicate override to check", ["paid"] = "Paid", ["all"] = "All",
    };
}

public sealed class InwardDashboard
{
    public long Open { get; set; }
    public long Overdue { get; set; }
    public long DueSoon { get; set; }
    public long UnassignedOld { get; set; }
    public long HeldByInactive { get; set; }
    public long ReceivedToday { get; set; }
    public List<CountRow> ByHolder { get; set; } = new();
    public List<CountRow> ByVertical { get; set; } = new();
    public List<CountRow> ByOffice { get; set; } = new();
    public List<CountRow> Ageing { get; set; } = new();
    public List<CountRow> Received { get; set; } = new();   // Key = yyyy-MM
    public List<CountRow> Closed { get; set; } = new();
}

public sealed class InvoiceDashboard
{
    public long Unpaid { get; set; }
    public long OutstandingPaise { get; set; }
    public long Msme45 { get; set; }
    public long MsmeSoon { get; set; }
    public long OnHold { get; set; }
    public long Overrides { get; set; }
    public long ClosedShortThisMonth { get; set; }
    public List<CountRow> Ageing { get; set; } = new();     // N = count, N2 = outstanding paise
    public List<CountRow> ByProject { get; set; } = new();
    public List<CountRow> ByVendor { get; set; } = new();
    public List<CountRow> ByHolder { get; set; } = new();
    public List<CountRow> Received { get; set; } = new();   // Key = yyyy-MM, N = count, N2 = total paise
    public List<CountRow> Paid { get; set; } = new();
}

public sealed class MsmeRow
{
    public long ItemId { get; set; }
    public string Number { get; set; } = "";
    public string VendorName { get; set; } = "";
    public string? VendorGstin { get; set; }
    public string? UdyamNo { get; set; }
    public string? MsmeClass { get; set; }
    public string InvoiceNo { get; set; } = "";
    public string InvoiceDate { get; set; } = "";
    public string DueOn { get; set; } = "";
    public long EffectivePaise { get; set; }
    public long PaidByCutoff { get; set; }
    public string? LastPaidOn { get; set; }
    public bool IsConfidential { get; set; }
    public bool Full { get; set; }
    public long OutstandingAtCutoff => EffectivePaise - PaidByCutoff;
}

public sealed class OfficeOption
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }
    public bool IsActive { get; set; }
    public string NumberCode => string.IsNullOrEmpty(Code) ? "GEN" : Code;
}

/// <summary>Read-only queries of the inward register (DD-08 §6). Every query applies <see cref="InwardAccess"/> visibility and masking.</summary>
public sealed class InwardRepository(Db db, InwardAccess access, InwardOptions o, Fmt fmt)
{
    public const int PageSize = 50;
    public const int MaxExport = 20_000;

    private string Today => fmt.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private string Day(int add) => fmt.Today.AddDays(add).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    /// <summary>UTC ISO time of the start of an office day (for comparing with stored UTC times).</summary>
    public string DayStartUtc(DateOnly d) => new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Utc).AddMinutes(-fmt.OffsetMinutes).ToString("o", CultureInfo.InvariantCulture);
    private string Mod => $"{fmt.OffsetMinutes:+0;-0} minutes";

    private string Select(ClaimsPrincipal u) => $"""
        SELECT i.*, h.DisplayName AS HolderName, COALESCE(h.IsActive, 1) AS HolderActive, h.Vertical AS HolderVertical,
               d.VendorName, d.InvoiceNo, d.InvoiceStatus, d.TotalPaise, d.AdjustPaise, d.SettledPaise, d.IsMsme,
               ({access.FullSql(u)}) AS Full
        FROM InwardItems i LEFT JOIN Users h ON h.Id = i.HolderUserId LEFT JOIN InvoiceDetails d ON d.ItemId = i.Id
        """;

    // ---------------------------------------------------------------- single records

    public InwardItem? Item(long id, ClaimsPrincipal u) => db.Query<InwardItem>(Select(u) + " WHERE i.Id = @id", new { id, uid = u.UserId() }).FirstOrDefault();
    public InwardItem? ByNumber(string number, ClaimsPrincipal u) =>
        db.Query<InwardItem>(Select(u) + " WHERE i.Number = @n COLLATE NOCASE", new { n = number.Trim(), uid = u.UserId() }).FirstOrDefault();
    public InvoiceDetail? Invoice(long itemId) => db.Single<InvoiceDetail>("SELECT * FROM InvoiceDetails WHERE ItemId = @itemId", new { itemId });
    public List<InvoicePayment> Payments(long itemId) => db.Query<InvoicePayment>("""
        SELECT p.*, EXISTS (SELECT 1 FROM InvoicePayments r WHERE r.ReversesId = p.Id) AS Reversed FROM InvoicePayments p WHERE p.ItemId = @itemId ORDER BY p.Id
        """, new { itemId });
    public List<InvoiceAdjustment> Adjustments(long itemId) => db.Query<InvoiceAdjustment>("SELECT * FROM InvoiceAdjustments WHERE ItemId = @itemId ORDER BY Id", new { itemId });
    public List<InwardMovement> Movements(long itemId) => db.Query<InwardMovement>("SELECT * FROM InwardMovements WHERE ItemId = @itemId ORDER BY Id", new { itemId });
    public List<InwardNote> Notes(long itemId) => db.Query<InwardNote>("SELECT * FROM InwardNotes WHERE ItemId = @itemId ORDER BY Id", new { itemId });
    public List<PersonOption> Copies(long itemId) => db.Query<PersonOption>(
        "SELECT u.Id, u.DisplayName, u.Username FROM InwardCopies c JOIN Users u ON u.Id = c.UserId WHERE c.ItemId = @itemId ORDER BY u.DisplayName", new { itemId });
    public List<InwardItem> Reminders(long itemId, ClaimsPrincipal u) => db.Query<InwardItem>(Select(u) + " WHERE i.LinkedToId = @itemId ORDER BY i.Id", new { itemId, uid = u.UserId() });

    public List<PersonOption> ActiveUsers() => db.Query<PersonOption>("SELECT Id, DisplayName, Username FROM Users WHERE IsActive = 1 ORDER BY DisplayName");
    public List<PersonOption> AllUsers() => db.Query<PersonOption>(
        "SELECT Id, DisplayName || CASE WHEN IsActive = 0 THEN ' (inactive)' ELSE '' END AS DisplayName, Username FROM Users ORDER BY IsActive DESC, DisplayName");

    public List<OfficeOption> Offices(bool activeOnly = true) => db.Query<OfficeOption>(
        "SELECT Id, Name, Code, IsActive FROM MasterItems WHERE Type = 'Location'" + (activeOnly ? " AND IsActive = 1" : "") + " ORDER BY SortOrder, Name");
    public OfficeOption? Office(long id) => db.Single<OfficeOption>("SELECT Id, Name, Code, IsActive FROM MasterItems WHERE Type = 'Location' AND Id = @id", new { id });

    /// <summary>The user's own office (DD-08 §6.3): operator grant, else directory location by name, else the default office name.</summary>
    public OfficeOption? OwnOffice(ClaimsPrincipal u)
    {
        var offices = Offices();
        var op = access.OperatorOffices(u).Select(id => offices.FirstOrDefault(x => x.Id == id)).FirstOrDefault(x => x != null);
        if (op != null) return op;
        var loc = db.Scalar<string>("SELECT s.Location FROM Users u JOIN Staff s ON s.StaffId = u.StaffId WHERE u.Id = @id", new { id = u.UserId() });
        var byLoc = offices.FirstOrDefault(x => string.Equals(x.Name.Trim(), (loc ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
        return byLoc ?? offices.FirstOrDefault(x => string.Equals(x.Name, o.DefaultOfficeName, StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- register

    private (string where, object args) RegisterWhere(ClaimsPrincipal u, InwardFilter f)
    {
        var w = new List<string> { access.VisibleSql(u) };
        if (f.OfficeId > 0) w.Add("i.OfficeId = @OfficeId");
        if (DateOnly.TryParseExact(f.From ?? "", "yyyy-MM-dd", out var from)) w.Add("i.ReceivedAt >= @fromUtc");
        if (DateOnly.TryParseExact(f.To ?? "", "yyyy-MM-dd", out var to)) w.Add("i.ReceivedAt < @toUtc");
        if (f.Type != null && InwardType.Labels.ContainsKey(f.Type)) w.Add("i.ItemType = @Type");
        if (f.Mode != null && InwardMode.Labels.ContainsKey(f.Mode)) w.Add("i.Mode = @Mode");
        if (f.HolderId > 0) w.Add("i.HolderUserId = @HolderId");
        if (InwardPriority.All.Contains(f.Priority)) w.Add("i.Priority = @Priority");
        w.Add((f.Status ?? "open") switch
        {
            "unassigned" => "i.Status = 'Unassigned'",
            "withholder" => "i.Status = 'WithHolder'",
            "withaccounts" => "i.Status = 'WithAccounts'",
            "overdue" => "i.Status IN ('Unassigned','WithHolder','WithAccounts') AND i.ReplyDueOn IS NOT NULL AND i.ReplyDueOn < @today",
            "closed" => "i.Status = 'Closed'",
            "all" => "1 = 1",
            _ => "i.Status IN ('Unassigned','WithHolder','WithAccounts')",
        });
        var fts = SearchText.ToFtsQuery(f.Q);
        // FTS first, newest first and capped, so a broad word never drags in the whole register
        if (fts != null) w.Add("i.Id IN (SELECT rowid FROM InwardSearch WHERE InwardSearch MATCH @fts ORDER BY rowid DESC LIMIT 5000)");
        return (string.Join(" AND ", w), new
        {
            uid = u.UserId(), f.OfficeId, f.Type, f.Mode, f.HolderId, f.Priority, today = Today, fts,
            fromUtc = f.From != null && DateOnly.TryParseExact(f.From, "yyyy-MM-dd", out from) ? DayStartUtc(from) : null,
            toUtc = f.To != null && DateOnly.TryParseExact(f.To, "yyyy-MM-dd", out to) ? DayStartUtc(to.AddDays(1)) : null,
        });
    }

    public Paged<InwardItem> Register(ClaimsPrincipal u, InwardFilter f)
    {
        var (where, args) = RegisterWhere(u, f);
        var page = Math.Max(1, f.Page);
        var total = db.Scalar<long>($"SELECT COUNT(*) FROM InwardItems i WHERE {where}", args);
        var items = db.Query<InwardItem>(Select(u) + $" WHERE {where} ORDER BY i.ReceivedAt DESC, i.Id DESC LIMIT {PageSize} OFFSET {(page - 1) * PageSize}", args);
        return new Paged<InwardItem> { Items = items, Total = total, Page = page, PageSize = PageSize };
    }

    /// <summary>All rows for an export or print; null when more than <paramref name="cap"/> match.</summary>
    public List<InwardItem>? RegisterRows(ClaimsPrincipal u, InwardFilter f, int cap = MaxExport, bool oldestFirst = false)
    {
        var (where, args) = RegisterWhere(u, f);
        if (db.Scalar<long>($"SELECT COUNT(*) FROM InwardItems i WHERE {where}", args) > cap) return null;
        return db.Query<InwardItem>(Select(u) + $" WHERE {where} ORDER BY i.ReceivedAt {(oldestFirst ? "" : "DESC")}, i.Id", args);
    }

    /// <summary>My items: held by me (open), copied to me, inwarded by me, closed in the last 90 days.</summary>
    public List<InwardItem> Mine(ClaimsPrincipal u, string tab)
    {
        var where = tab switch
        {
            "copied" => "EXISTS (SELECT 1 FROM InwardCopies c WHERE c.ItemId = i.Id AND c.UserId = @uid)",
            "inwarded" => "i.InwardedByUserId = @uid",
            "closed" => "i.Status = 'Closed' AND i.ClosedAt >= @since AND EXISTS (SELECT 1 FROM InwardParticipants ip WHERE ip.ItemId = i.Id AND ip.UserId = @uid)",
            _ => "i.HolderUserId = @uid AND i.Status = 'WithHolder'",
        };
        return db.Query<InwardItem>(Select(u) + $" WHERE {where} ORDER BY i.ReceivedAt DESC LIMIT 500",
            new { uid = u.UserId(), since = DateTime.UtcNow.AddDays(-90).ToString("o") });
    }

    /// <summary>The office day that is <paramref name="n"/> working days (Mon–Fri) before today.</summary>
    public static DateOnly WorkingDaysBefore(DateOnly today, int n)
    {
        var d = today;
        while (n > 0) { d = d.AddDays(-1); if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) n--; }
        return d;
    }

    // ---------------------------------------------------------------- dashboards

    private sealed class OpenGroup
    {
        public long? H { get; set; }
        public long O { get; set; }
        public string OfficeName { get; set; } = "";
        public string Status { get; set; } = "";
        public string Age { get; set; } = "";
        public long N { get; set; }
        public long Overdue { get; set; }
        public long DueSoon { get; set; }
        public long Old { get; set; }
    }

    private sealed class UserRow { public long Id { get; set; } public string DisplayName { get; set; } = ""; public string? Vertical { get; set; } public bool IsActive { get; set; } }

    /// <summary>
    /// Dashboard (§6.1). The open items are grouped twice (by holder and status; by office and age bucket) and every tile
    /// and breakdown is summed from those groups; age buckets compare the stored UTC times with day boundaries (no date
    /// functions per row). Benchmarked at 250,000 items (InwardBenchTests).
    /// </summary>
    public InwardDashboard Dashboard(ClaimsPrincipal u, long? officeId)
    {
        var vis = access.VisibleSql(u) + (officeId > 0 ? " AND i.OfficeId = @officeId" : "");
        var t = fmt.Today;
        var a = new
        {
            uid = u.UserId(), officeId, today = Today, soon = Day(1),
            oldCut = DayStartUtc(WorkingDaysBefore(t, Math.Max(1, o.UnassignedAlertWorkingDays)).AddDays(1)),
            a7 = DayStartUtc(t.AddDays(-7)), a15 = DayStartUtc(t.AddDays(-15)), a30 = DayStartUtc(t.AddDays(-30)),
            todayUtc = DayStartUtc(t), mod = Mod,
            fromUtc = DayStartUtc(new DateOnly(t.AddMonths(-11).Year, t.AddMonths(-11).Month, 1)),
        };
        // two small groupings of the open items: by holder (≤ users × statuses rows) and by office × age (≤ offices × 12 rows)
        var groups = db.Query<OpenGroup>($"""
            SELECT i.HolderUserId AS H, 0 AS O, '' AS OfficeName, i.Status, '' AS Age,
                   COUNT(*) AS N, COALESCE(SUM(i.ReplyDueOn < @today), 0) AS Overdue, COALESCE(SUM(i.ReplyDueOn BETWEEN @today AND @soon), 0) AS DueSoon,
                   COALESCE(SUM(i.Status = 'Unassigned' AND i.InwardedAt < @oldCut), 0) AS Old
            FROM InwardItems i WHERE {vis} AND i.Status IN ('Unassigned','WithHolder','WithAccounts')
            GROUP BY i.HolderUserId, i.Status
            """, a);
        var places = db.Query<OpenGroup>($"""
            SELECT i.OfficeId AS O, MAX(i.OfficeName) AS OfficeName,
                   CASE WHEN i.ReceivedAt >= @a7 THEN '0–7' WHEN i.ReceivedAt >= @a15 THEN '8–15' WHEN i.ReceivedAt >= @a30 THEN '16–30' ELSE '> 30' END AS Age, COUNT(*) AS N
            FROM InwardItems i WHERE {vis} AND i.Status IN ('Unassigned','WithHolder','WithAccounts')
            GROUP BY i.OfficeId + 0, Age
            """, a);
        var people = db.Query<UserRow>("SELECT Id, DisplayName, Vertical, IsActive FROM Users").ToDictionary(x => x.Id);
        UserRow? P(long? id) => id is long k && people.TryGetValue(k, out var p) ? p : null;
        var d = new InwardDashboard
        {
            Open = groups.Sum(g => g.N), Overdue = groups.Sum(g => g.Overdue), DueSoon = groups.Sum(g => g.DueSoon), UnassignedOld = groups.Sum(g => g.Old),
            HeldByInactive = groups.Where(g => g.Status == ItemStatus.WithHolder && P(g.H) is { IsActive: false }).Sum(g => g.N),
            // a separate index-only count: an OR with the open-status condition would make SQLite scan the whole register
            ReceivedToday = db.Scalar<long>($"SELECT COUNT(*) FROM InwardItems i WHERE {vis} AND i.InwardedAt >= @todayUtc", a),
        };
        d.ByHolder = groups.GroupBy(g => g.Status == ItemStatus.WithHolder ? g.H : null)
            .Select(x => new CountRow
            {
                Key = x.Key is long h ? P(h)?.DisplayName ?? "#" + h : "(unassigned / Accounts)", Key2 = x.Key?.ToString(CultureInfo.InvariantCulture),
                N = x.Sum(g => g.N), N2 = x.Sum(g => g.Overdue),
            }).OrderByDescending(r => r.N).Take(20).ToList();
        d.ByVertical = groups.GroupBy(g => g.Status switch
            {
                ItemStatus.Unassigned => "(registry)", ItemStatus.WithAccounts => "(Accounts)",
                _ => string.IsNullOrEmpty(P(g.H)?.Vertical) ? "(not set)" : P(g.H)!.Vertical!,
            })
            .Select(x => new CountRow { Key = x.Key, N = x.Sum(g => g.N) }).OrderByDescending(r => r.N).Take(30).ToList();
        d.ByOffice = places.GroupBy(g => g.O).Select(x => new CountRow { Key = x.First().OfficeName, N = x.Sum(g => g.N) }).OrderByDescending(r => r.N).ToList();
        d.Ageing = places.GroupBy(g => g.Age).Select(x => new CountRow { Key = x.Key, N = x.Sum(g => g.N) }).ToList();
        d.Received = db.Query<CountRow>($"SELECT {MonthCase("i.InwardedAt")} AS Key, COUNT(*) AS N FROM InwardItems i WHERE {vis} AND i.InwardedAt >= @fromUtc GROUP BY Key", a);
        d.Closed = db.Query<CountRow>($"SELECT {MonthCase("i.ClosedAt")} AS Key, COUNT(*) AS N FROM InwardItems i WHERE {vis} AND i.ClosedAt >= @fromUtc GROUP BY Key", a);
        return d;
    }

    /// <summary>
    /// SQL giving the office month (yyyy-MM) of a stored UTC time over the last 12 months, by comparing with the months'
    /// UTC start times (cheaper than strftime per row). The boundaries are generated here, never user input.
    /// </summary>
    private string MonthCase(string col)
    {
        var sb = new System.Text.StringBuilder("CASE");
        var first = new DateOnly(fmt.Today.Year, fmt.Today.Month, 1);
        for (var k = 0; k < 12; k++)
        {
            var m = first.AddMonths(-k);
            sb.Append($" WHEN {col} >= '{DayStartUtc(m)}' THEN '{m:yyyy-MM}'");
        }
        return sb.Append(" END").ToString();
    }

    private sealed class InvTiles
    {
        public long Unpaid { get; set; }
        public long OutstandingPaise { get; set; }
        public long Msme45 { get; set; }
        public long MsmeSoon { get; set; }
        public long OnHold { get; set; }
        public long Overrides { get; set; }
        public long ClosedShortThisMonth { get; set; }
    }

    private const string InvJoin = "FROM InwardItems i JOIN InvoiceDetails d ON d.ItemId = i.Id LEFT JOIN Users h ON h.Id = i.HolderUserId";
    private const string InvOpen = "d.InvoiceStatus NOT IN " + InvStatus.FinalSql;

    private object InvArgs(ClaimsPrincipal u) => new
    {
        uid = u.UserId(), today = Today, msmeCut = Day(-o.MsmeMaxDays), msmeSoon = Day(o.MsmeDueSoonDays), mod = Mod,
        monthStartUtc = DayStartUtc(new DateOnly(fmt.Today.Year, fmt.Today.Month, 1)),
        fromUtc = DayStartUtc(new DateOnly(fmt.Today.AddMonths(-11).Year, fmt.Today.AddMonths(-11).Month, 1)),
        d30 = Day(-30), d45 = Day(-45), d60 = Day(-60), d90 = Day(-90),
    };

    public InvoiceDashboard InvoiceDashboard(ClaimsPrincipal u)
    {
        var vis = access.VisibleSql(u);
        var full = access.FullSql(u);
        var a = InvArgs(u);
        var t = db.Query<InvTiles>($"""
            SELECT COALESCE(SUM({InvOpen}), 0) AS Unpaid,
                   COALESCE(SUM(CASE WHEN {InvOpen} THEN d.TotalPaise + d.AdjustPaise - d.SettledPaise ELSE 0 END), 0) AS OutstandingPaise,
                   COALESCE(SUM({InvOpen} AND d.IsMsme = 1 AND d.InvoiceDate < @msmeCut), 0) AS Msme45,
                   COALESCE(SUM({InvOpen} AND d.IsMsme = 1 AND d.PaymentDueOn BETWEEN @today AND @msmeSoon), 0) AS MsmeSoon,
                   COALESCE(SUM(d.InvoiceStatus = 'OnHold'), 0) AS OnHold,
                   COALESCE(SUM({InvOpen} AND d.OverrideReason IS NOT NULL AND d.DuplicateCheckedBy IS NULL), 0) AS Overrides,
                   COALESCE(SUM(d.InvoiceStatus = 'ClosedShort' AND d.SettledAt >= @monthStartUtc), 0) AS ClosedShortThisMonth
            {InvJoin} WHERE {vis}
            """, a).First();
        var r = new InvoiceDashboard
        {
            Unpaid = t.Unpaid, OutstandingPaise = t.OutstandingPaise, Msme45 = t.Msme45, MsmeSoon = t.MsmeSoon, OnHold = t.OnHold,
            Overrides = t.Overrides, ClosedShortThisMonth = t.ClosedShortThisMonth,
        };
        r.Ageing = db.Query<CountRow>($"""
            SELECT CASE WHEN d.InvoiceDate >= @d30 THEN '0–30' WHEN d.InvoiceDate >= @d45 THEN '31–45' WHEN d.InvoiceDate >= @d60 THEN '46–60'
                        WHEN d.InvoiceDate >= @d90 THEN '61–90' ELSE '> 90' END AS Key, COUNT(*) AS N, SUM(d.TotalPaise + d.AdjustPaise - d.SettledPaise) AS N2
            {InvJoin} WHERE {vis} AND {InvOpen} GROUP BY Key
            """, a);
        r.ByProject = db.Query<CountRow>($"""
            SELECT COALESCE(d.ProjectCode, '(none)') AS Key, COUNT(*) AS N, SUM(d.TotalPaise + d.AdjustPaise - d.SettledPaise) AS N2
            {InvJoin} WHERE {vis} AND {InvOpen} GROUP BY Key ORDER BY N2 DESC LIMIT 30
            """, a);
        // confidential vendors are grouped as "Confidential" for users without full access (DD-08 E8)
        r.ByVendor = db.Query<CountRow>($"""
            SELECT CASE WHEN i.IsConfidential = 1 AND NOT ({full}) THEN 'Confidential' ELSE d.VendorName END AS Key, COUNT(*) AS N,
                   SUM(d.TotalPaise + d.AdjustPaise - d.SettledPaise) AS N2
            {InvJoin} WHERE {vis} AND {InvOpen} GROUP BY Key ORDER BY N2 DESC LIMIT 20
            """, a);
        r.ByHolder = db.Query<CountRow>($"""
            SELECT CASE WHEN i.Status = 'WithAccounts' THEN '(Accounts)' WHEN i.Status = 'Unassigned' THEN '(registry)' ELSE COALESCE(h.DisplayName, '(none)') END AS Key,
                   COUNT(*) AS N, SUM(d.TotalPaise + d.AdjustPaise - d.SettledPaise) AS N2
            {InvJoin} WHERE {vis} AND {InvOpen} GROUP BY Key ORDER BY N DESC LIMIT 20
            """, a);
        r.Received = db.Query<CountRow>($"SELECT strftime('%Y-%m', i.InwardedAt, @mod) AS Key, COUNT(*) AS N, SUM(d.TotalPaise) AS N2 {InvJoin} WHERE {vis} AND i.InwardedAt >= @fromUtc GROUP BY Key", a);
        r.Paid = db.Query<CountRow>($"SELECT strftime('%Y-%m', d.SettledAt, @mod) AS Key, COUNT(*) AS N, SUM(d.SettledPaise) AS N2 {InvJoin} WHERE {vis} AND d.InvoiceStatus = 'Paid' AND d.SettledAt >= @fromUtc GROUP BY Key", a);
        return r;
    }

    private (string where, object args) InvoiceWhere(ClaimsPrincipal u, InvoiceFilter f)
    {
        var w = new List<string> { access.VisibleSql(u) };
        w.Add((f.Status ?? "open") switch
        {
            "accounts" => "d.InvoiceStatus IN ('SentToAccounts','PartPaid')",
            "msme45" => $"{InvOpen} AND d.IsMsme = 1 AND d.InvoiceDate < @msmeCut",
            "msmesoon" => $"{InvOpen} AND d.IsMsme = 1 AND d.PaymentDueOn BETWEEN @today AND @msmeSoon",
            "hold" => "d.InvoiceStatus = 'OnHold'",
            "override" => $"{InvOpen} AND d.OverrideReason IS NOT NULL AND d.DuplicateCheckedBy IS NULL",
            "paid" => "d.InvoiceStatus = 'Paid'",
            "all" => "1 = 1",
            _ => InvOpen,
        });
        if (!string.IsNullOrWhiteSpace(f.Project)) w.Add("d.ProjectCode = @project COLLATE NOCASE");
        var fts = SearchText.ToFtsQuery(f.Q);
        if (fts != null) w.Add("i.Id IN (SELECT rowid FROM InwardSearch WHERE InwardSearch MATCH @fts ORDER BY rowid DESC LIMIT 5000)");
        return (string.Join(" AND ", w), new { uid = u.UserId(), today = Today, msmeCut = Day(-o.MsmeMaxDays), msmeSoon = Day(o.MsmeDueSoonDays), project = f.Project, fts });
    }

    public Paged<InwardItem> Invoices(ClaimsPrincipal u, InvoiceFilter f)
    {
        var (where, args) = InvoiceWhere(u, f);
        var page = Math.Max(1, f.Page);
        var total = db.Scalar<long>($"SELECT COUNT(*) {InvJoin} WHERE {where}", args);
        var items = db.Query<InwardItem>(Select(u) + $" WHERE i.ItemType = 'Invoice' AND {where} ORDER BY d.InvoiceDate, i.Id LIMIT {PageSize} OFFSET {(page - 1) * PageSize}", args);
        return new Paged<InwardItem> { Items = items, Total = total, Page = page, PageSize = PageSize };
    }

    public sealed class InvoiceRow
    {
        public long ItemId { get; set; }
        public string Number { get; set; } = "";
        public bool IsConfidential { get; set; }
        public bool Full { get; set; }
        public string? HolderName { get; set; }
        public string Status { get; set; } = "";
        public string VendorName { get; set; } = "";
        public string? VendorGstin { get; set; }
        public bool IsMsme { get; set; }
        public string? MsmeClass { get; set; }
        public string InvoiceNo { get; set; } = "";
        public string InvoiceDate { get; set; } = "";
        public string? ProjectCode { get; set; }
        public string? PoRef { get; set; }
        public long TotalPaise { get; set; }
        public long AdjustPaise { get; set; }
        public long SettledPaise { get; set; }
        public string InvoiceStatus { get; set; } = "";
        public string? CertifiedOn { get; set; }
        public string PaymentDueOn { get; set; } = "";
    }

    public List<InvoiceRow>? InvoiceRows(ClaimsPrincipal u, InvoiceFilter f)
    {
        var (where, args) = InvoiceWhere(u, f);
        if (db.Scalar<long>($"SELECT COUNT(*) {InvJoin} WHERE {where}", args) > MaxExport) return null;
        return db.Query<InvoiceRow>($"""
            SELECT i.Id AS ItemId, i.Number, i.IsConfidential, ({access.FullSql(u)}) AS Full, h.DisplayName AS HolderName, i.Status, d.*
            {InvJoin} WHERE {where} ORDER BY d.InvoiceDate, i.Id
            """, args);
    }

    /// <summary>
    /// MSME / 43B(h) report (DD-08 §3.4): Micro and Small invoices (class recorded or not, traders excluded) dated on or
    /// before the as-on date that were outstanding on it, or paid after their MSME due date. Payments count only when
    /// dated on or before the as-on date and entered on or before <paramref name="enteredBy"/> (for reproducibility).
    /// </summary>
    public List<MsmeRow> MsmeReport(ClaimsPrincipal u, DateOnly asOn, DateOnly? enteredBy)
    {
        var a = new
        {
            uid = u.UserId(), asOn = asOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            entered = DayStartUtc((enteredBy ?? fmt.Today).AddDays(1)),
        };
        return db.Query<MsmeRow>($"""
            SELECT i.Id AS ItemId, i.Number, i.IsConfidential, ({access.FullSql(u)}) AS Full, d.VendorName, d.VendorGstin, d.UdyamNo, d.MsmeClass,
                   d.InvoiceNo, d.InvoiceDate, d.PaymentDueOn AS DueOn,
                   d.TotalPaise + COALESCE((SELECT SUM(CASE WHEN x.Kind = 'DebitNote' THEN x.AmountPaise ELSE -x.AmountPaise END)
                                            FROM InvoiceAdjustments x WHERE x.ItemId = i.Id AND x.DocDate <= @asOn AND x.EnteredAt < @entered), 0) AS EffectivePaise,
                   COALESCE((SELECT SUM(p.NetPaise + p.TdsPaise + p.OtherDeductionsPaise) FROM InvoicePayments p
                             WHERE p.ItemId = i.Id AND p.PaidOn <= @asOn AND p.EnteredAt < @entered), 0) AS PaidByCutoff,
                   (SELECT MAX(p.PaidOn) FROM InvoicePayments p WHERE p.ItemId = i.Id AND p.ReversesId IS NULL AND p.EnteredAt < @entered) AS LastPaidOn
            {InvJoin}
            WHERE {access.VisibleSql(u)} AND d.IsMsme = 1 AND d.IsTrader = 0 AND COALESCE(d.MsmeClass, '') <> 'Medium'
              AND d.InvoiceDate <= @asOn AND d.InvoiceStatus NOT IN ('Cancelled','ReturnedToVendor','Rejected')
            ORDER BY d.MsmeClass IS NULL, d.VendorName, d.InvoiceDate
            """, a).Where(r => r.OutstandingAtCutoff > 0 || (r.LastPaidOn != null && string.CompareOrdinal(r.LastPaidOn, r.DueOn) > 0)).ToList();
    }

    // ---------------------------------------------------------------- project codes, members

    public List<ProjectCode> ProjectCodes(bool activeOnly) => db.Query<ProjectCode>(
        "SELECT p.*, u.DisplayName AS OwnerName FROM ProjectCodes p LEFT JOIN Users u ON u.Id = p.OwnerUserId" + (activeOnly ? " WHERE p.IsActive = 1" : "") + " ORDER BY p.Code");

    public List<InwardMember> Members() => db.Query<InwardMember>("""
        SELECT m.*, u.DisplayName, u.Username, u.IsActive, l.Name AS OfficeName FROM InwardMembers m JOIN Users u ON u.Id = m.UserId
        LEFT JOIN MasterItems l ON l.Id = m.OfficeId ORDER BY m.Role, l.Name, u.DisplayName
        """);

    public List<InwardMemberChange> MemberHistory(int limit = 100) => db.Query<InwardMemberChange>("""
        SELECT h.*, COALESCE(u.DisplayName, '#' || h.UserId) AS DisplayName FROM InwardMemberHistory h LEFT JOIN Users u ON u.Id = h.UserId ORDER BY h.Id DESC LIMIT @limit
        """, new { limit });

    /// <summary>Offices that have no active Inward Operator (shown as a warning on the Access page).</summary>
    public List<string> OfficesWithoutOperator() => db.Strings("""
        SELECT l.Name FROM MasterItems l WHERE l.Type = 'Location' AND l.IsActive = 1
          AND NOT EXISTS (SELECT 1 FROM InwardMembers m JOIN Users u ON u.Id = m.UserId WHERE m.Role = 'InwardOperator' AND m.OfficeId = l.Id AND u.IsActive = 1)
          AND EXISTS (SELECT 1 FROM InwardItems i WHERE i.OfficeId = l.Id)
        ORDER BY l.Name
        """);

    // ---------------------------------------------------------------- My Desk

    /// <summary>SQL for DeskService's single count query. Parameters: @uid, @today, @iSoon, @iAcceptCut, @iOffices (comma list or 0), @iAccounts.</summary>
    public static string DeskMineSql => """
        (SELECT COUNT(*) FROM InwardItems i WHERE i.HolderUserId = @uid AND i.Status = 'WithHolder'
           AND (i.ItemType = 'Invoice' OR i.AcceptedAt IS NULL OR (i.ReplyDueOn IS NOT NULL AND i.ReplyDueOn <= @iSoon)))
        """;

    public static string DeskAssignedSql => "(SELECT EXISTS (SELECT 1 FROM InwardParticipants ip WHERE ip.UserId = @uid))";

    public static string DeskQueueSql => "CASE WHEN @iAccounts = 1 THEN (SELECT COUNT(*) FROM InwardItems i WHERE i.Status = 'WithAccounts') ELSE 0 END";

    public List<InwardItem> DeskMine(ClaimsPrincipal u, int limit = 10) => db.Query<InwardItem>(Select(u) + """
         WHERE i.HolderUserId = @uid AND i.Status = 'WithHolder'
           AND (i.ItemType = 'Invoice' OR i.AcceptedAt IS NULL OR (i.ReplyDueOn IS NOT NULL AND i.ReplyDueOn <= @soon))
         ORDER BY COALESCE(i.ReplyDueOn, '9999'), i.ReceivedAt LIMIT @limit
        """, new { uid = u.UserId(), soon = Day(1), limit });

    public long UnassignedCount(ClaimsPrincipal u)
    {
        var offices = access.OperatorOffices(u);
        return offices.Count == 0 ? 0 : db.Scalar<long>($"SELECT COUNT(*) FROM InwardItems WHERE Status = 'Unassigned' AND OfficeId IN ({string.Join(",", offices)})");
    }

    public List<InwardItem> DeskUnassigned(ClaimsPrincipal u, int limit = 10)
    {
        var offices = access.OperatorOffices(u);
        if (offices.Count == 0) return new();
        return db.Query<InwardItem>(Select(u) + $" WHERE i.Status = 'Unassigned' AND i.OfficeId IN ({string.Join(",", offices)}) ORDER BY i.InwardedAt LIMIT @limit", new { uid = u.UserId(), limit });
    }

    public List<InwardItem> DeskQueue(ClaimsPrincipal u, int limit = 10) =>
        access.IsAccounts(u) ? db.Query<InwardItem>(Select(u) + " WHERE i.Status = 'WithAccounts' ORDER BY i.ReceivedAt LIMIT @limit", new { uid = u.UserId(), limit }) : new();
}
