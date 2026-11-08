using System.Globalization;
using System.Security.Claims;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

public sealed class ComplianceEventInput
{
    public string? SourceType { get; set; }
    public long? ForumId { get; set; }
    public string? EventNo { get; set; }
    public string? Title { get; set; }
    public string? HeldOn { get; set; }
    public string? PeriodFrom { get; set; }
    public string? PeriodTo { get; set; }
    public string? ReportReceivedOn { get; set; }
    public string? ReportedTo { get; set; }
    public string? ReportingCycle { get; set; }
    public string? NextReportingOn { get; set; }
    public bool IsConfidential { get; set; }
    public string? SourceKeptAt { get; set; }
    public string? Remarks { get; set; }
}

public sealed class CompliancePointInput
{
    public string? RefNo { get; set; }
    public string? RefSubject { get; set; }
    public string? PointText { get; set; }
    public string? Category { get; set; }
    public string? Priority { get; set; }
    public string? OwnerVertical { get; set; }
    public long PrimaryOfficerUserId { get; set; }
    public List<long> SupportOfficerIds { get; set; } = new();
    public string? OfficerCode { get; set; }
    public string? RaisedOn { get; set; }
    public string? DueDate { get; set; }
    public double? Amount { get; set; }
    public string? CarriedFromNumber { get; set; }
    public string? PapersKeptAt { get; set; }
    /// <summary>Create only: Ongoing (default) or PendingExternal, for migrating points that are already open.</summary>
    public string? InitialStatus { get; set; }
    public string? InitialAtrText { get; set; }
    public string? InitialAsOn { get; set; }
}

/// <summary>
/// Every state change of the compliance module (DD-07 §3.3): one BEGIN IMMEDIATE transaction each, with RowVersion
/// checks, a ComplianceHistory row, in-app notifications inside the transaction, and an audit entry after commit.
/// Notifications and audit details never carry compliance free text (DD-07 §5 rule 4).
/// </summary>
public sealed class ComplianceWorkflow(Db db, ComplianceAccess access, ComplianceRepository repo, ComplianceOptions o, MasterRepository masters,
    AuditService audit, INotificationService notifications, ChangeTracker tracker, Fmt fmt, ILogger<ComplianceWorkflow> log)
{
    private const string Changed = "This action point was changed by someone else. Please reload the page and try again.";

    private string Today => fmt.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ================================================================= helpers

    private static string? Date(string label, string? v, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(v)) { if (required) throw new WorkflowException($"{label} is required."); return null; }
        var t = v.Trim();
        string[] formats = { "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "dd-MM-yyyy", "d/M/yyyy", "d.M.yyyy", "d-M-yyyy" };
        if (!DateOnly.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) || d.Year < 1990 || d.Year > 2100)
            throw new WorkflowException($"{label}: enter a date as dd.MM.yyyy.");
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string? ParseDate(string label, string? v, bool required = false) => Date(label, v, required);

    private string Label(CompliancePoint p) => access.IsRestricted(p.SourceType, p.IsConfidential) ? "Restricted" : ComplianceSource.Label(p.SourceType);

    private NotificationMessage Msg(CompliancePoint p, string type, string what, string? link = null) =>
        new("Compliance." + type, $"Action point {p.Number}: {what}", $"{Label(p)}{(string.IsNullOrEmpty(p.RefNo) ? "" : " · Ref " + p.RefNo)}",
            link ?? $"/Compliance/Point/{p.Id}", "CompliancePoint", p.Id);

    private void Audit(string action, CompliancePoint p, string detail) =>
        audit.Log("Compliance." + action, "CompliancePoint", p.Id, $"{p.Number}; {(access.IsRestricted(p.SourceType, p.IsConfidential) ? "restricted" : ComplianceSource.Label(p.SourceType))}; {detail}");

    private static void History(SqliteConnection c, SqliteTransaction tx, long? pointId, long? eventId, ClaimsPrincipal? u, string action, string? from, string? to, string? detail) =>
        Db.Exec(c, tx, """
            INSERT INTO ComplianceHistory (PointId, EventId, At, ActorUserId, ActorName, Action, FromStatus, ToStatus, Detail)
            VALUES (@pointId, @eventId, @at, @uid, @name, @action, @from, @to, @detail)
            """, new { pointId, eventId, at = Db.Now(), uid = u?.UserId(), name = u?.DisplayName() ?? "system", action, from, to, detail });

    private static NotificationActor Actor(ClaimsPrincipal u) => NotificationActor.From(u);

    private static CompliancePoint Load(SqliteConnection c, SqliteTransaction tx, long id) =>
        Db.Query<CompliancePoint>(c, tx, ComplianceRepository.PointSelect + " WHERE p.Id = @id", new { id }).FirstOrDefault()
        ?? throw new WorkflowException("Action point not found.");

    private static List<long> Officers(SqliteConnection c, SqliteTransaction tx, CompliancePoint p) =>
        new[] { p.PrimaryOfficerUserId }.Concat(Db.Query<IdRow>(c, tx, "SELECT UserId AS Id FROM ComplianceSupportOfficers WHERE PointId = @Id", new { p.Id }).Select(r => r.Id)).Distinct().ToList();

    private sealed class IdRow { public long Id { get; set; } }

    /// <summary>Only a Compliance Admin who is not an officer of the point may take a Cell action (segregation of duties).</summary>
    private void RequireCell(SqliteConnection c, SqliteTransaction tx, CompliancePoint p, ClaimsPrincipal u)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only the Compliance Cell can do this.");
        if (!access.CanViewPoint(p, u)) throw new WorkflowException("Action point not found.");
        if (ComplianceAccess.IsOfficer(c, tx, p.Id, u.UserId()))
            throw new WorkflowException("You are an officer on this action point, so another Compliance Cell member must act on it.");
    }

    private static void RequireOpen(CompliancePoint p)
    {
        if (!p.IsOpen) throw new WorkflowException($"This action point is {p.Status.ToLowerInvariant()}; it must be reopened first.");
    }

    private static void Bump(SqliteConnection c, SqliteTransaction tx, CompliancePoint p, long rowVersion, string set, Dictionary<string, object?> args)
    {
        args["Id"] = p.Id; args["rv"] = rowVersion; args["now"] = Db.Now();
        var n = Db.Exec(c, tx, $"UPDATE CompliancePoints SET {set}, RowVersion = RowVersion + 1, UpdatedAt = @now WHERE Id = @Id AND RowVersion = @rv", args);
        if (n == 0) throw new WorkflowException(Changed);
    }

    private void TouchAll(IEnumerable<long> users)
    {
        foreach (var id in users) tracker.TouchUser(id);
        tracker.Touch(ChangeTracker.ComplianceQueue);
    }

    private static T InTx<T>(Db db, Func<SqliteConnection, SqliteTransaction, T> f)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction(deferred: false);
        var r = f(c, tx);
        tx.Commit();
        return r;
    }

    /// <summary>
    /// When the next periodic update is due (DD-07 §8): from the latest accepted update's as-on date (else the date
    /// raised), per the event's reporting cycle; never after the current due date, and never before today.
    /// </summary>
    public static string? NextUpdateDue(string cycle, string? nextReportingOn, int leadDays, string baseDate, string currentDue, DateOnly today)
    {
        if (!DateOnly.TryParseExact(baseDate, "yyyy-MM-dd", out var b)) b = today;
        DateOnly? next = cycle switch
        {
            ComplianceCycle.Monthly => b.AddMonths(1),
            ComplianceCycle.Quarterly => b.AddMonths(3),
            ComplianceCycle.NextMeeting when DateOnly.TryParseExact(nextReportingOn ?? "", "yyyy-MM-dd", out var m) && m > today
                => m.AddDays(-leadDays) > today ? m.AddDays(-leadDays) : today,
            _ => null,
        };
        if (next == null) return null;
        if (DateOnly.TryParseExact(currentDue, "yyyy-MM-dd", out var due) && next > due) next = due;
        if (next < today) next = today;
        return next.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private string? NextUpdateDue(CompliancePoint p, string? baseDate = null) =>
        NextUpdateDue(p.ReportingCycle, p.NextReportingOn, o.UpdateLeadDays, baseDate ?? p.AtrAsOn ?? p.RaisedOn, p.CurrentDueDate, fmt.Today);

    // ================================================================= events

    private ComplianceEvent ValidateEvent(ComplianceEventInput i, ComplianceEvent? existing)
    {
        if (!ComplianceSource.IsValid(i.SourceType)) throw new WorkflowException("Choose the source type.");
        var forum = i.ForumId is > 0 ? masters.Get(i.ForumId.Value) : null;
        if (forum == null || forum.Type != MasterTypes.ComplianceForum || (!forum.IsActive && forum.Id != existing?.ForumId))
            throw new WorkflowException("Choose the forum / body from the list.");
        var cycle = i.ReportingCycle ?? ComplianceCycle.NextMeeting;
        if (!ComplianceCycle.Labels.ContainsKey(cycle)) throw new WorkflowException("Choose the reporting cycle.");
        var e = new ComplianceEvent
        {
            SourceType = i.SourceType!, ForumId = forum.Id, Forum = forum.Name,
            EventNo = ComplianceText.Clean("Event number", i.EventNo, 60),
            Title = ComplianceText.Required("Title", i.Title, 300),
            HeldOn = Date("Held on", i.HeldOn), PeriodFrom = Date("Period from", i.PeriodFrom), PeriodTo = Date("Period to", i.PeriodTo),
            ReportReceivedOn = Date("Report received on", i.ReportReceivedOn),
            ReportedTo = ComplianceText.Clean("Compliance reported to", i.ReportedTo, 150),
            ReportingCycle = cycle, NextReportingOn = Date("Next reporting date", i.NextReportingOn),
            IsConfidential = i.IsConfidential,
            SourceKeptAt = ComplianceText.Clean("Source document kept at", i.SourceKeptAt, 300),
            Remarks = ComplianceText.Clean("Remarks", i.Remarks, 1000),
        };
        if (e.HeldOn == null && e.PeriodTo == null && e.ReportReceivedOn == null)
            throw new WorkflowException("Enter the date held, the period covered, or the date the report was received.");
        if (e.PeriodFrom != null && e.PeriodTo != null && string.CompareOrdinal(e.PeriodFrom, e.PeriodTo) > 0)
            throw new WorkflowException("The period cannot end before it starts.");
        return e;
    }

    public long CreateEvent(ComplianceEventInput i, ClaimsPrincipal u)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only the Compliance Cell can register events.");
        var e = ValidateEvent(i, null);
        var now = Db.Now();
        var id = InTx(db, (c, tx) =>
        {
            var id = Db.Insert(c, tx, """
                INSERT INTO ComplianceEvents (SourceType, ForumId, Forum, EventNo, Title, HeldOn, PeriodFrom, PeriodTo, ReportReceivedOn, ReportedTo,
                    ReportingCycle, NextReportingOn, IsConfidential, SourceKeptAt, Remarks, CreatedByUserId, CreatedByName, CreatedAt, UpdatedAt)
                VALUES (@SourceType, @ForumId, @Forum, @EventNo, @Title, @HeldOn, @PeriodFrom, @PeriodTo, @ReportReceivedOn, @ReportedTo,
                    @ReportingCycle, @NextReportingOn, @IsConfidential, @SourceKeptAt, @Remarks, @uid, @name, @now, @now)
                """, new
            {
                e.SourceType, e.ForumId, e.Forum, e.EventNo, e.Title, e.HeldOn, e.PeriodFrom, e.PeriodTo, e.ReportReceivedOn, e.ReportedTo,
                e.ReportingCycle, e.NextReportingOn, e.IsConfidential, e.SourceKeptAt, e.Remarks, uid = u.UserId(), name = u.DisplayName(), now,
            });
            History(c, tx, null, id, u, "EventCreated", null, null, $"{e.Forum}: {e.Title}");
            return id;
        });
        audit.Log("Compliance.EventCreated", "ComplianceEvent", id, $"event #{id}; {(access.IsRestricted(e.SourceType, e.IsConfidential) ? "restricted" : ComplianceSource.Label(e.SourceType))}");
        return id;
    }

    public void UpdateEvent(long id, ComplianceEventInput i, long rowVersion, ClaimsPrincipal u)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only the Compliance Cell can edit events.");
        var old = repo.Event(id) ?? throw new WorkflowException("Event not found.");
        if (!access.CanSeeEventManaged(old, u)) throw new WorkflowException("Event not found.");
        var e = ValidateEvent(i, old);
        var touched = new List<long>();
        InTx(db, (c, tx) =>
        {
            var hasPoints = Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM CompliancePoints WHERE EventId = @id", new { id }) > 0;
            if (hasPoints && e.SourceType != old.SourceType) throw new WorkflowException("The source type cannot change once the event has action points.");
            var n = Db.Exec(c, tx, """
                UPDATE ComplianceEvents SET SourceType = @SourceType, ForumId = @ForumId, Forum = @Forum, EventNo = @EventNo, Title = @Title,
                    HeldOn = @HeldOn, PeriodFrom = @PeriodFrom, PeriodTo = @PeriodTo, ReportReceivedOn = @ReportReceivedOn, ReportedTo = @ReportedTo,
                    ReportingCycle = @ReportingCycle, NextReportingOn = @NextReportingOn, IsConfidential = @IsConfidential,
                    SourceKeptAt = @SourceKeptAt, Remarks = @Remarks, UpdatedAt = @now, RowVersion = RowVersion + 1
                WHERE Id = @id AND RowVersion = @rowVersion
                """, new
            {
                e.SourceType, e.ForumId, e.Forum, e.EventNo, e.Title, e.HeldOn, e.PeriodFrom, e.PeriodTo, e.ReportReceivedOn, e.ReportedTo,
                e.ReportingCycle, e.NextReportingOn, e.IsConfidential, e.SourceKeptAt, e.Remarks, now = Db.Now(), id, rowVersion,
            });
            if (n == 0) throw new WorkflowException("This event was changed by someone else. Please reload and try again.");
            var changes = new List<string>();
            void Cmp(string f, string? a, string? b) { if ((a ?? "") != (b ?? "")) changes.Add($"{f}: '{a}' → '{b}'"); }
            Cmp("Forum", old.Forum, e.Forum); Cmp("Event no.", old.EventNo, e.EventNo); Cmp("Title", old.Title, e.Title); Cmp("Held on", old.HeldOn, e.HeldOn);
            Cmp("Period", $"{old.PeriodFrom}–{old.PeriodTo}", $"{e.PeriodFrom}–{e.PeriodTo}"); Cmp("Report received", old.ReportReceivedOn, e.ReportReceivedOn);
            Cmp("Reported to", old.ReportedTo, e.ReportedTo); Cmp("Cycle", old.ReportingCycle, e.ReportingCycle); Cmp("Next reporting", old.NextReportingOn, e.NextReportingOn);
            Cmp("Confidential", old.IsConfidential ? "yes" : "no", e.IsConfidential ? "yes" : "no"); Cmp("Source kept at", old.SourceKeptAt, e.SourceKeptAt); Cmp("Remarks", old.Remarks, e.Remarks);
            History(c, tx, null, id, u, "EventUpdated", null, null, changes.Count == 0 ? "no change" : string.Join("; ", changes));
            // the reporting cycle and next date drive when updates are due
            foreach (var p in Db.Query<CompliancePoint>(c, tx, ComplianceRepository.PointSelect + $" WHERE p.EventId = @id AND p.Status IN {CStatus.OpenSql}", new { id }))
            {
                var next = NextUpdateDue(p);
                if (next != p.NextUpdateDue) Db.Exec(c, tx, "UPDATE CompliancePoints SET NextUpdateDue = @next, UpdateRemindedFor = NULL WHERE Id = @Id", new { next, p.Id });
                touched.Add(p.PrimaryOfficerUserId);
            }
            return 0;
        });
        TouchAll(touched.Distinct());
        audit.Log("Compliance.EventUpdated", "ComplianceEvent", id, $"event #{id}; {(access.IsRestricted(e.SourceType, e.IsConfidential) ? "restricted" : ComplianceSource.Label(e.SourceType))}");
    }

    // ================================================================= points: create, edit, import

    private sealed class CleanPoint
    {
        public string? RefNo, RefSubject, Category, OwnerVertical, OfficerCode, PapersKeptAt, CarriedFromNumber;
        public string PointText = "", Priority = "Medium", RaisedOn = "", DueDate = "";
        public long Primary; public List<long> Support = new(); public double? Amount; public long? CarriedFromId;
        public string Status = CStatus.Ongoing; public string? AtrText, AtrAsOn;
    }

    private CleanPoint ValidatePoint(SqliteConnection c, SqliteTransaction tx, CompliancePointInput i, ComplianceEvent ev, CompliancePoint? existing, ClaimsPrincipal u, bool creating)
    {
        var r = new CleanPoint
        {
            RefNo = ComplianceText.Clean("Reference no.", i.RefNo, 60),
            RefSubject = ComplianceText.Clean("Subject", i.RefSubject, 300),
            PointText = ComplianceText.Required("Action point", i.PointText, ComplianceText.LongText),
            OfficerCode = ComplianceText.Clean("Officer code", i.OfficerCode, 30),
            PapersKeptAt = ComplianceText.Clean("Supporting papers kept at", i.PapersKeptAt, 300),
            Priority = string.IsNullOrWhiteSpace(i.Priority) ? "Medium" : i.Priority.Trim(),
        };
        r.Priority = CompliancePriority.All.FirstOrDefault(x => x.Equals(r.Priority, StringComparison.OrdinalIgnoreCase)) ?? throw new WorkflowException("Priority must be High, Medium or Low.");
        string? Master(string type, string label, string? v, string? current)
        {
            var t = ComplianceText.Clean(label, v, 150);
            if (t == null) return null;
            if (current != null && t.Equals(current, StringComparison.OrdinalIgnoreCase)) return current;
            var hit = masters.List(type).FirstOrDefault(m => m.Name.Equals(t, StringComparison.OrdinalIgnoreCase));
            return hit?.Name ?? throw new WorkflowException($"{label} '{t}' is not in the list.");
        }
        r.Category = Master(MasterTypes.ComplianceCategory, "Category", i.Category, existing?.Category);
        r.OwnerVertical = Master(MasterTypes.Vertical, "Owner vertical", i.OwnerVertical, existing?.OwnerVertical);
        bool Active(long id) => Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM Users WHERE Id = @id AND IsActive = 1", new { id }) > 0;
        if (i.PrimaryOfficerUserId <= 0 || (!Active(i.PrimaryOfficerUserId) && i.PrimaryOfficerUserId != existing?.PrimaryOfficerUserId))
            throw new WorkflowException("Choose an active primary dealing officer.");
        r.Primary = i.PrimaryOfficerUserId;
        r.Support = i.SupportOfficerIds.Where(x => x > 0 && x != r.Primary).Distinct().ToList();
        if (r.Primary == u.UserId() || r.Support.Contains(u.UserId()))
            throw new WorkflowException("You cannot make yourself a dealing officer: the Compliance Cell member who assigns a point must not also answer for it.");
        if (r.Support.Count > 10) throw new WorkflowException("At most 10 supporting officers.");
        foreach (var s in r.Support) if (!Active(s)) throw new WorkflowException("Supporting officers must be active users.");
        if (i.Amount is < 0 or > 1e13) throw new WorkflowException("The amount is not valid.");
        r.Amount = i.Amount;
        r.CarriedFromNumber = ComplianceText.Clean("Carried from", i.CarriedFromNumber, 30);
        if (r.CarriedFromNumber != null)
        {
            var from = Db.Query<CompliancePoint>(c, tx, ComplianceRepository.PointSelect + " WHERE p.Number = @n COLLATE NOCASE", new { n = r.CarriedFromNumber }).FirstOrDefault();
            if (from == null || !access.CanViewPoint(from, u) || from.Id == existing?.Id) throw new WorkflowException($"Carried-from point {r.CarriedFromNumber} was not found.");
            if (access.IsRestricted(from.SourceType, from.IsConfidential) != access.IsRestricted(ev.SourceType, ev.IsConfidential))
                throw new WorkflowException("A point can only be carried from a point with the same restriction (restricted / not restricted).");
            r.CarriedFromId = from.Id;
        }
        if (creating)
        {
            r.RaisedOn = Date("Raised on", i.RaisedOn) ?? ev.HeldOn ?? ev.ReportReceivedOn ?? Today;
            r.DueDate = Date("Due date", i.DueDate, true)!;
            r.Status = string.IsNullOrWhiteSpace(i.InitialStatus) ? CStatus.Ongoing : i.InitialStatus.Trim();
            if (r.Status is not (CStatus.Ongoing or CStatus.PendingExternal)) throw new WorkflowException("The initial status must be Ongoing or PendingExternal.");
            r.AtrText = ComplianceText.Clean("Action taken so far", i.InitialAtrText, ComplianceText.LongText);
            r.AtrAsOn = Date("As on", i.InitialAsOn);
            if (r.AtrText != null && r.AtrAsOn == null) throw new WorkflowException("Enter the 'as on' date of the action taken so far.");
            if (r.AtrAsOn != null && string.CompareOrdinal(r.AtrAsOn, Today) > 0) throw new WorkflowException("The 'as on' date cannot be in the future.");
            if (r.AtrText == null) r.AtrAsOn = null;
        }
        return r;
    }

    private (long id, string number) InsertPoint(SqliteConnection c, SqliteTransaction tx, ComplianceEvent ev, CleanPoint r, ClaimsPrincipal u, string now, DateOnly? on = null)
    {
        var fy = LeadRepository.FinancialYear(on ?? fmt.Today);
        var serial = Db.Scalar<long>(c, tx, """
            INSERT INTO ComplianceSerials (FinancialYear, LastSerial) VALUES (@fy, 1)
            ON CONFLICT(FinancialYear) DO UPDATE SET LastSerial = LastSerial + 1 RETURNING LastSerial
            """, new { fy });
        var number = $"CMP/{fy}/{serial:D5}";
        var seq = Db.Scalar<long>(c, tx, "SELECT COALESCE(MAX(Seq), 0) + 1 FROM CompliancePoints WHERE EventId = @Id", new { ev.Id });
        var next = NextUpdateDue(ev.ReportingCycle, ev.NextReportingOn, o.UpdateLeadDays, r.AtrAsOn ?? r.RaisedOn, r.DueDate, fmt.Today);
        var id = Db.Insert(c, tx, """
            INSERT INTO CompliancePoints (EventId, Number, FinancialYear, Seq, RefNo, RefSubject, PointText, Category, Priority, OwnerVertical,
                PrimaryOfficerUserId, OfficerCode, RaisedOn, OriginalDueDate, CurrentDueDate, NextUpdateDue, Amount, CarriedFromId, Status,
                AtrText, AtrAsOn, PapersKeptAt, CreatedByName, CreatedAt, UpdatedAt)
            VALUES (@EventId, @number, @fy, @seq, @RefNo, @RefSubject, @PointText, @Category, @Priority, @OwnerVertical,
                @Primary, @OfficerCode, @RaisedOn, @DueDate, @DueDate, @next, @Amount, @CarriedFromId, @Status,
                @AtrText, @AtrAsOn, @PapersKeptAt, @name, @now, @now)
            """, new
        {
            EventId = ev.Id, number, fy, seq, r.RefNo, r.RefSubject, r.PointText, r.Category, r.Priority, r.OwnerVertical, r.Primary, r.OfficerCode,
            r.RaisedOn, r.DueDate, next, r.Amount, r.CarriedFromId, r.Status, r.AtrText, r.AtrAsOn, r.PapersKeptAt, name = u.DisplayName(), now,
        });
        foreach (var s in r.Support) Db.Exec(c, tx, "INSERT OR IGNORE INTO ComplianceSupportOfficers (PointId, UserId) VALUES (@id, @s)", new { id, s });
        History(c, tx, id, ev.Id, u, "Created", null, r.Status, $"Due {r.DueDate}" + (r.AtrText != null ? $"; action taken so far as on {r.AtrAsOn}: {r.AtrText}" : ""));
        return (id, number);
    }

    /// <param name="on">The creation date that sets the FY of the number (tests only; default today).</param>
    public long CreatePoint(long eventId, CompliancePointInput i, ClaimsPrincipal u, DateOnly? on = null)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only the Compliance Cell can add action points.");
        var ev = repo.Event(eventId) ?? throw new WorkflowException("Event not found.");
        if (!access.CanSeeEventManaged(ev, u)) throw new WorkflowException("Event not found.");
        CompliancePoint? p = null;
        var officers = new List<long>();
        var id = InTx(db, (c, tx) =>
        {
            var r = ValidatePoint(c, tx, i, ev, null, u, true);
            var (id, _) = InsertPoint(c, tx, ev, r, u, Db.Now(), on);
            p = Load(c, tx, id);
            officers = Officers(c, tx, p);
            notifications.Notify(c, tx, officers, Actor(u), Msg(p, "Assigned", "assigned to you"));
            return id;
        });
        TouchAll(officers);
        Audit("PointCreated", p!, $"event #{eventId}");
        return id;
    }

    public void UpdatePoint(long id, CompliancePointInput i, long rowVersion, ClaimsPrincipal u)
    {
        CompliancePoint? p = null;
        var notify = new List<long>();
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, id);
            RequireCell(c, tx, p, u);
            RequireOpen(p);
            var ev = repo.Event(p.EventId)!;
            var oldOfficers = Officers(c, tx, p);
            var r = ValidatePoint(c, tx, i, ev, p, u, false);
            var changes = new List<string>();
            void Cmp(string f, object? a, object? b) { if ($"{a}" != $"{b}") changes.Add($"{f}: '{a}' → '{b}'"); }
            Cmp("Ref no.", p.RefNo, r.RefNo); Cmp("Subject", p.RefSubject, r.RefSubject); Cmp("Action point", p.PointText, r.PointText);
            Cmp("Category", p.Category, r.Category); Cmp("Priority", p.Priority, r.Priority); Cmp("Vertical", p.OwnerVertical, r.OwnerVertical);
            Cmp("Officer code", p.OfficerCode, r.OfficerCode); Cmp("Amount", p.Amount, r.Amount); Cmp("Carried from", p.CarriedFromId, r.CarriedFromId);
            Cmp("Papers kept at", p.PapersKeptAt, r.PapersKeptAt);
            Bump(c, tx, p, rowVersion, """
                RefNo = @RefNo, RefSubject = @RefSubject, PointText = @PointText, Category = @Category, Priority = @Priority, OwnerVertical = @OwnerVertical,
                OfficerCode = @OfficerCode, Amount = @Amount, CarriedFromId = @CarriedFromId, PapersKeptAt = @PapersKeptAt
                """, new()
            {
                ["RefNo"] = r.RefNo, ["RefSubject"] = r.RefSubject, ["PointText"] = r.PointText, ["Category"] = r.Category, ["Priority"] = r.Priority,
                ["OwnerVertical"] = r.OwnerVertical, ["OfficerCode"] = r.OfficerCode, ["Amount"] = r.Amount, ["CarriedFromId"] = r.CarriedFromId, ["PapersKeptAt"] = r.PapersKeptAt,
            });
            notify = SetOfficers(c, tx, p, r.Primary, r.Support, u, changes);
            History(c, tx, p.Id, p.EventId, u, "Edited", p.Status, p.Status, changes.Count == 0 ? "no change" : string.Join("; ", changes));
            notify.AddRange(oldOfficers);
            return 0;
        });
        TouchAll(notify.Distinct());
        Audit("PointUpdated", p!, "edited");
    }

    /// <summary>Changes primary and supporting officers; returns everyone affected. Notifies newly assigned officers.</summary>
    private List<long> SetOfficers(SqliteConnection c, SqliteTransaction tx, CompliancePoint p, long primary, List<long> support, ClaimsPrincipal u, List<string> changes)
    {
        var before = Officers(c, tx, p);
        support = support.Where(x => x != primary).Distinct().ToList();
        if (primary != p.PrimaryOfficerUserId)
        {
            Db.Exec(c, tx, "UPDATE CompliancePoints SET PrimaryOfficerUserId = @primary, Reminded7For = NULL, Reminded1For = NULL, LastOverdueReminderOn = NULL, UpdateRemindedFor = NULL WHERE Id = @Id", new { primary, p.Id });
            changes.Add($"Primary officer: #{p.PrimaryOfficerUserId} → #{primary}");
        }
        var oldSupport = Db.Query<IdRow>(c, tx, "SELECT UserId AS Id FROM ComplianceSupportOfficers WHERE PointId = @Id", new { p.Id }).Select(x => x.Id).ToList();
        foreach (var s in oldSupport.Except(support)) Db.Exec(c, tx, "DELETE FROM ComplianceSupportOfficers WHERE PointId = @Id AND UserId = @s", new { p.Id, s });
        foreach (var s in support.Except(oldSupport)) Db.Exec(c, tx, "INSERT OR IGNORE INTO ComplianceSupportOfficers (PointId, UserId) VALUES (@Id, @s)", new { p.Id, s });
        if (!oldSupport.OrderBy(x => x).SequenceEqual(support.OrderBy(x => x))) changes.Add($"Supporting officers: {string.Join(",", oldSupport.Select(x => "#" + x))} → {string.Join(",", support.Select(x => "#" + x))}");
        var after = new[] { primary }.Concat(support).Distinct().ToList();
        var added = after.Except(before).ToList();
        if (added.Count > 0) notifications.Notify(c, tx, added, Actor(u), Msg(p, "Assigned", "assigned to you"));
        var removed = before.Except(after).ToList();
        if (removed.Count > 0) notifications.Notify(c, tx, removed, Actor(u), Msg(p, "Reassigned", "reassigned to another officer"));
        return before.Concat(after).Distinct().ToList();
    }

    public void Reassign(long id, long primary, List<long> support, long rowVersion, ClaimsPrincipal u)
    {
        CompliancePoint? p = null;
        var touched = new List<long>();
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, id);
            RequireCell(c, tx, p, u);
            RequireOpen(p);
            if (primary == u.UserId() || support.Contains(u.UserId())) throw new WorkflowException("You cannot make yourself a dealing officer on a point you manage.");
            if (Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM Users WHERE Id = @primary AND IsActive = 1", new { primary }) == 0) throw new WorkflowException("Choose an active primary officer.");
            foreach (var s in support) if (Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM Users WHERE Id = @s AND IsActive = 1", new { s }) == 0) throw new WorkflowException("Supporting officers must be active users.");
            Bump(c, tx, p, rowVersion, "Seq = Seq", new());
            var changes = new List<string>();
            touched = SetOfficers(c, tx, p, primary, support, u, changes);
            History(c, tx, p.Id, p.EventId, u, "Reassigned", p.Status, p.Status, changes.Count == 0 ? "no change" : string.Join("; ", changes));
            return 0;
        });
        TouchAll(touched);
        Audit("Reassigned", p!, "officers changed");
    }

    /// <summary>Inserts every imported row in one transaction (all or nothing); refuses a file already imported into the event.</summary>
    public List<string> ImportPoints(long eventId, IReadOnlyList<CompliancePointInput> rows, string sha256, ClaimsPrincipal u)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only the Compliance Cell can import action points.");
        var ev = repo.Event(eventId) ?? throw new WorkflowException("Event not found.");
        if (!access.CanSeeEventManaged(ev, u)) throw new WorkflowException("Event not found.");
        if (rows.Count == 0) throw new WorkflowException("The file has no rows.");
        var numbers = new List<string>();
        var officers = new List<long>();
        InTx(db, (c, tx) =>
        {
            if (Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM ComplianceImports WHERE EventId = @eventId AND Sha256 = @sha256", new { eventId, sha256 }) > 0)
                throw new WorkflowException("This file has already been imported into this event.");
            var now = Db.Now();
            var k = 0;
            foreach (var row in rows)
            {
                k++;
                CleanPoint r;
                try { r = ValidatePoint(c, tx, row, ev, null, u, true); }
                catch (WorkflowException ex) { throw new WorkflowException($"Row {k}: {ex.Message}"); }
                var (id, number) = InsertPoint(c, tx, ev, r, u, now);
                numbers.Add(number);
                officers.Add(r.Primary);
                officers.AddRange(r.Support);
            }
            // one notification per officer for the whole import
            foreach (var g in officers.GroupBy(x => x))
                notifications.Notify(c, tx, new[] { g.Key }, Actor(u), new NotificationMessage("Compliance.Assigned",
                    $"{g.Count()} action point(s) assigned to you", $"Imported into event #{eventId}", "/Compliance/Mine"));
            Db.Exec(c, tx, "INSERT INTO ComplianceImports (EventId, Sha256, Rows, ImportedByName, ImportedAt) VALUES (@eventId, @sha256, @n, @name, @now)",
                new { eventId, sha256, n = rows.Count, name = u.DisplayName(), now });
            History(c, tx, null, eventId, u, "Imported", null, null, $"{rows.Count} action points from a CSV file (SHA-256 {sha256})");
            return 0;
        });
        TouchAll(officers.Distinct());
        audit.Log("Compliance.PointsImported", "ComplianceEvent", eventId, $"event #{eventId}; {rows.Count} points; {numbers.FirstOrDefault()}…{numbers.LastOrDefault()}; file SHA-256 {sha256}");
        return numbers;
    }

    // ================================================================= officer: updates and revisions

    public long SubmitUpdate(long pointId, string? asOn, string? text, string? proposedStatus, string? papersKeptAt, long rowVersion, ClaimsPrincipal u)
    {
        var date = Date("As on", asOn, true)!;
        if (string.CompareOrdinal(date, Today) > 0) throw new WorkflowException("The 'as on' date cannot be in the future.");
        var body = ComplianceText.Required("Compliance", text, ComplianceText.LongText);
        var papers = ComplianceText.Clean("Supporting papers kept at", papersKeptAt, 300);
        var status = proposedStatus ?? "";
        if (status is not (CStatus.Ongoing or CStatus.PendingExternal or CStatus.ProposedClosure)) throw new WorkflowException("Choose the proposed status.");
        CompliancePoint? p = null;
        var updateId = InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            if (!ComplianceAccess.IsOfficer(c, tx, p.Id, u.UserId())) throw new WorkflowException("Only the dealing officers can submit compliance for this point.");
            if (p.Status is not (CStatus.Ongoing or CStatus.PendingExternal)) throw new WorkflowException("Compliance can be submitted only while the point is ongoing or pending with an external party.");
            if (p.PendingUpdateId != null) throw new WorkflowException("An update is already awaiting review by the Compliance Cell.");
            var id = Db.Insert(c, tx, """
                INSERT INTO ComplianceUpdates (PointId, SubmittedByUserId, SubmittedByName, SubmittedAt, AsOn, UpdateText, ProposedStatus, PapersKeptAt)
                VALUES (@pointId, @uid, @name, @now, @date, @body, @status, @papers)
                """, new { pointId, uid = u.UserId(), name = u.DisplayName(), now = Db.Now(), date, body, status, papers });
            Bump(c, tx, p, rowVersion, "PendingUpdateId = @upd, LastReturnedAt = NULL", new() { ["upd"] = id });
            History(c, tx, p.Id, p.EventId, u, "UpdateSubmitted", p.Status, p.Status, $"Update #{id} as on {date}, proposed {status}");
            notifications.Notify(c, tx, access.ActiveAdminIds(), Actor(u), Msg(p, "UpdateSubmitted", "compliance submitted for review", "/Compliance/Review"));
            return id;
        });
        TouchAll(new[] { p!.PrimaryOfficerUserId, u.UserId() });
        Audit("UpdateSubmitted", p, $"update #{updateId}; proposed {status}");
        return updateId;
    }

    public void WithdrawUpdate(long pointId, long updateId, ClaimsPrincipal u)
    {
        CompliancePoint? p = null;
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            var upd = Db.Query<ComplianceUpdate>(c, tx, "SELECT * FROM ComplianceUpdates WHERE Id = @updateId AND PointId = @pointId", new { updateId, pointId }).FirstOrDefault()
                      ?? throw new WorkflowException("Update not found.");
            if (upd.SubmittedByUserId != u.UserId()) throw new WorkflowException("Only the officer who submitted it can withdraw an update.");
            if (upd.ReviewState != "Pending") throw new WorkflowException("This update has already been reviewed.");
            Db.Exec(c, tx, "UPDATE ComplianceUpdates SET ReviewState = 'Withdrawn', ReviewedAt = @now, ReviewedByName = @name, ReviewedByUserId = @uid WHERE Id = @updateId",
                new { updateId, now = Db.Now(), name = u.DisplayName(), uid = u.UserId() });
            Db.Exec(c, tx, "UPDATE CompliancePoints SET PendingUpdateId = NULL, RowVersion = RowVersion + 1, UpdatedAt = @now WHERE Id = @pointId AND PendingUpdateId = @updateId",
                new { pointId, updateId, now = Db.Now() });
            History(c, tx, p.Id, p.EventId, u, "UpdateWithdrawn", p.Status, p.Status, $"Update #{updateId}");
            return 0;
        });
        TouchAll(new[] { p!.PrimaryOfficerUserId, u.UserId() });
        Audit("UpdateWithdrawn", p, $"update #{updateId}");
    }

    public void AcceptUpdate(long pointId, long updateId, string? atrText, long rowVersion, ClaimsPrincipal u)
    {
        CompliancePoint? p = null;
        ComplianceUpdate? upd = null;
        var touched = new List<long>();
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            RequireCell(c, tx, p, u);
            upd = PendingUpdate(c, tx, p, updateId);
            if (upd.SubmittedByUserId == u.UserId()) throw new WorkflowException("You cannot review your own update.");
            var vetted = ComplianceText.Clean("Action taken (ATR wording)", atrText, ComplianceText.LongText) ?? upd.UpdateText;
            var now = Db.Now();
            Db.Exec(c, tx, "UPDATE ComplianceUpdates SET ReviewState = 'Accepted', ReviewedByUserId = @uid, ReviewedByName = @name, ReviewedAt = @now, AtrText = @vetted WHERE Id = @updateId",
                new { updateId, uid = u.UserId(), name = u.DisplayName(), now, vetted });
            var next = NextUpdateDue(p, upd.AsOn);
            Bump(c, tx, p, rowVersion, "Status = @status, AtrText = @vetted, AtrAsOn = @asOn, PapersKeptAt = COALESCE(@papers, PapersKeptAt), PendingUpdateId = NULL, NextUpdateDue = @next, UpdateRemindedFor = NULL",
                new() { ["status"] = upd.ProposedStatus, ["vetted"] = vetted, ["asOn"] = upd.AsOn, ["papers"] = upd.PapersKeptAt, ["next"] = next });
            History(c, tx, p.Id, p.EventId, u, "UpdateAccepted", p.Status, upd.ProposedStatus,
                $"Update #{updateId} as on {upd.AsOn}" + (vetted != upd.UpdateText ? $"; ATR wording: {vetted}" : ""));
            touched = Officers(c, tx, p);
            notifications.Notify(c, tx, new[] { upd.SubmittedByUserId, p.PrimaryOfficerUserId }, Actor(u), Msg(p, "UpdateAccepted", "compliance accepted"));
            return 0;
        });
        TouchAll(touched);
        Audit("UpdateAccepted", p!, $"update #{updateId}; {p!.Status} → {upd!.ProposedStatus}");
    }

    public void ReturnUpdate(long pointId, long updateId, string? remarks, long rowVersion, ClaimsPrincipal u)
    {
        var why = ComplianceText.Required("Remarks", remarks, ComplianceText.Remarks);
        CompliancePoint? p = null;
        var touched = new List<long>();
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            RequireCell(c, tx, p, u);
            var upd = PendingUpdate(c, tx, p, updateId);
            if (upd.SubmittedByUserId == u.UserId()) throw new WorkflowException("You cannot review your own update.");
            Db.Exec(c, tx, "UPDATE ComplianceUpdates SET ReviewState = 'Returned', ReviewedByUserId = @uid, ReviewedByName = @name, ReviewedAt = @now, ReviewRemarks = @why WHERE Id = @updateId",
                new { updateId, uid = u.UserId(), name = u.DisplayName(), now = Db.Now(), why });
            Bump(c, tx, p, rowVersion, "PendingUpdateId = NULL, LastReturnedAt = @now2", new() { ["now2"] = Db.Now() });
            History(c, tx, p.Id, p.EventId, u, "UpdateReturned", p.Status, p.Status, $"Update #{updateId}: {why}");
            touched = Officers(c, tx, p);
            notifications.Notify(c, tx, new[] { upd.SubmittedByUserId, p.PrimaryOfficerUserId }, Actor(u), Msg(p, "UpdateReturned", "compliance returned for revision"));
            return 0;
        });
        TouchAll(touched);
        Audit("UpdateReturned", p!, $"update #{updateId}");
    }

    private static ComplianceUpdate PendingUpdate(SqliteConnection c, SqliteTransaction tx, CompliancePoint p, long updateId)
    {
        var upd = Db.Query<ComplianceUpdate>(c, tx, "SELECT * FROM ComplianceUpdates WHERE Id = @updateId AND PointId = @Id", new { updateId, p.Id }).FirstOrDefault()
                  ?? throw new WorkflowException("Update not found.");
        if (upd.ReviewState != "Pending" || p.PendingUpdateId != upd.Id) throw new WorkflowException("This update has already been dealt with.");
        return upd;
    }

    public void RejectClosureProposal(long pointId, string? remarks, long rowVersion, ClaimsPrincipal u)
    {
        var why = ComplianceText.Required("Remarks", remarks, ComplianceText.Remarks);
        CompliancePoint? p = null;
        var touched = new List<long>();
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            RequireCell(c, tx, p, u);
            if (p.Status != CStatus.ProposedClosure) throw new WorkflowException("This point is not proposed for closure.");
            Bump(c, tx, p, rowVersion, "Status = 'Ongoing'", new());
            History(c, tx, p.Id, p.EventId, u, "ClosureProposalRejected", p.Status, CStatus.Ongoing, why);
            touched = Officers(c, tx, p);
            notifications.Notify(c, tx, touched, Actor(u), Msg(p, "ClosureProposalRejected", "closure proposal not accepted; point remains ongoing"));
            return 0;
        });
        TouchAll(touched);
        Audit("ClosureProposalRejected", p!, "ProposedClosure → Ongoing");
    }

    public long RequestRevision(long pointId, string? newDate, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        var date = Date("New due date", newDate, true)!;
        if (string.CompareOrdinal(date, Today) < 0) throw new WorkflowException("The new due date cannot be in the past.");
        var why = ComplianceText.Required("Reason", reason, ComplianceText.Remarks);
        CompliancePoint? p = null;
        var id = InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            if (p.PrimaryOfficerUserId != u.UserId()) throw new WorkflowException("Only the primary dealing officer can request a new due date.");
            RequireOpen(p);
            if (p.PendingRevisionId != null) throw new WorkflowException("A due-date revision is already awaiting a decision.");
            if (date == p.CurrentDueDate) throw new WorkflowException("That is already the due date.");
            var id = Db.Insert(c, tx, """
                INSERT INTO ComplianceRevisions (PointId, RequestedByUserId, RequestedByName, RequestedAt, OldDate, NewDate, Reason)
                VALUES (@pointId, @uid, @name, @now, @old, @date, @why)
                """, new { pointId, uid = u.UserId(), name = u.DisplayName(), now = Db.Now(), old = p.CurrentDueDate, date, why });
            Bump(c, tx, p, rowVersion, "PendingRevisionId = @rev", new() { ["rev"] = id });
            History(c, tx, p.Id, p.EventId, u, "RevisionRequested", p.Status, p.Status, $"Revision #{id}: {p.CurrentDueDate} → {date}; {why}");
            notifications.Notify(c, tx, access.ActiveAdminIds(), Actor(u), Msg(p, "RevisionRequested", "new due date requested", "/Compliance/Review"));
            return id;
        });
        TouchAll(new[] { u.UserId() });
        Audit("RevisionRequested", p!, $"revision #{id}");
        return id;
    }

    public void WithdrawRevision(long pointId, long revisionId, ClaimsPrincipal u)
    {
        CompliancePoint? p = null;
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            var r = Db.Query<ComplianceRevision>(c, tx, "SELECT * FROM ComplianceRevisions WHERE Id = @revisionId AND PointId = @pointId", new { revisionId, pointId }).FirstOrDefault()
                    ?? throw new WorkflowException("Revision request not found.");
            if (r.RequestedByUserId != u.UserId()) throw new WorkflowException("Only the officer who asked can withdraw a revision request.");
            if (r.Status != "Pending") throw new WorkflowException("This request has already been decided.");
            Db.Exec(c, tx, "UPDATE ComplianceRevisions SET Status = 'Withdrawn', DecidedAt = @now, DecidedByName = @name, DecidedByUserId = @uid WHERE Id = @revisionId",
                new { revisionId, now = Db.Now(), name = u.DisplayName(), uid = u.UserId() });
            Db.Exec(c, tx, "UPDATE CompliancePoints SET PendingRevisionId = NULL, RowVersion = RowVersion + 1, UpdatedAt = @now WHERE Id = @pointId AND PendingRevisionId = @revisionId",
                new { pointId, revisionId, now = Db.Now() });
            History(c, tx, p.Id, p.EventId, u, "RevisionWithdrawn", p.Status, p.Status, $"Revision #{revisionId}");
            notifications.Notify(c, tx, access.ActiveAdminIds(), Actor(u), Msg(p, "RevisionWithdrawn", "due-date request withdrawn"));
            return 0;
        });
        TouchAll(new[] { u.UserId() });
        Audit("RevisionWithdrawn", p!, $"revision #{revisionId}");
    }

    public void DecideRevision(long pointId, long revisionId, bool approve, string? remarks, long rowVersion, ClaimsPrincipal u)
    {
        var why = approve ? ComplianceText.Clean("Remarks", remarks, ComplianceText.Remarks) : ComplianceText.Required("Remarks", remarks, ComplianceText.Remarks);
        CompliancePoint? p = null;
        ComplianceRevision? r = null;
        var touched = new List<long>();
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            RequireCell(c, tx, p, u);
            r = Db.Query<ComplianceRevision>(c, tx, "SELECT * FROM ComplianceRevisions WHERE Id = @revisionId AND PointId = @pointId", new { revisionId, pointId }).FirstOrDefault()
                ?? throw new WorkflowException("Revision request not found.");
            if (r.Status != "Pending" || p.PendingRevisionId != r.Id) throw new WorkflowException("This request has already been decided.");
            Db.Exec(c, tx, "UPDATE ComplianceRevisions SET Status = @s, DecidedByUserId = @uid, DecidedByName = @name, DecidedAt = @now, DecisionRemarks = @why WHERE Id = @revisionId",
                new { revisionId, s = approve ? "Approved" : "Rejected", uid = u.UserId(), name = u.DisplayName(), now = Db.Now(), why });
            if (approve)
            {
                p.CurrentDueDate = r.NewDate;
                Bump(c, tx, p, rowVersion, "CurrentDueDate = @d, PendingRevisionId = NULL, NextUpdateDue = @next, Reminded7For = NULL, Reminded1For = NULL, LastOverdueReminderOn = NULL, UpdateRemindedFor = NULL",
                    new() { ["d"] = r.NewDate, ["next"] = NextUpdateDue(p) });
            }
            else Bump(c, tx, p, rowVersion, "PendingRevisionId = NULL", new());
            History(c, tx, p.Id, p.EventId, u, approve ? "RevisionApproved" : "RevisionRejected", p.Status, p.Status,
                $"Revision #{revisionId}: {r.OldDate} → {r.NewDate}" + (why != null ? "; " + why : ""));
            touched = Officers(c, tx, p);
            notifications.Notify(c, tx, new[] { p.PrimaryOfficerUserId, r.RequestedByUserId }, Actor(u), Msg(p, "RevisionDecided", approve ? $"new due date {fmt.DateDot(r.NewDate)} approved" : "new due date not approved"));
            return 0;
        });
        TouchAll(touched);
        Audit(approve ? "RevisionApproved" : "RevisionRejected", p!, $"revision #{revisionId}");
    }

    public void ReviseDirect(long pointId, string? newDate, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        var date = Date("New due date", newDate, true)!;
        var why = ComplianceText.Required("Reason", reason, ComplianceText.Remarks);
        CompliancePoint? p = null;
        var touched = new List<long>();
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            RequireCell(c, tx, p, u);
            RequireOpen(p);
            if (date == p.CurrentDueDate) throw new WorkflowException("That is already the due date.");
            var now = Db.Now();
            Db.Exec(c, tx, "UPDATE ComplianceRevisions SET Status = 'Superseded', DecidedAt = @now, DecidedByName = @name, DecidedByUserId = @uid WHERE PointId = @pointId AND Status = 'Pending'",
                new { pointId, now, name = u.DisplayName(), uid = u.UserId() });
            var id = Db.Insert(c, tx, """
                INSERT INTO ComplianceRevisions (PointId, RequestedByUserId, RequestedByName, RequestedAt, OldDate, NewDate, Reason, Status, DecidedByUserId, DecidedByName, DecidedAt, DecisionRemarks)
                VALUES (@pointId, @uid, @name, @now, @old, @date, @why, 'Approved', @uid, @name, @now, 'Revised directly by the Compliance Cell')
                """, new { pointId, uid = u.UserId(), name = u.DisplayName(), now, old = p.CurrentDueDate, date, why });
            var old = p.CurrentDueDate;
            p.CurrentDueDate = date;
            Bump(c, tx, p, rowVersion, "CurrentDueDate = @d, PendingRevisionId = NULL, NextUpdateDue = @next, Reminded7For = NULL, Reminded1For = NULL, LastOverdueReminderOn = NULL, UpdateRemindedFor = NULL",
                new() { ["d"] = date, ["next"] = NextUpdateDue(p) });
            History(c, tx, p.Id, p.EventId, u, "DueDateRevised", p.Status, p.Status, $"Revision #{id}: {old} → {date}; {why}");
            touched = Officers(c, tx, p);
            notifications.Notify(c, tx, new[] { p.PrimaryOfficerUserId }, Actor(u), Msg(p, "RevisionDecided", $"due date changed to {fmt.DateDot(date)}"));
            return 0;
        });
        TouchAll(touched);
        Audit("DueDateRevised", p!, "due date revised");
    }

    // ================================================================= closure

    /// <summary>Pending update and revision become Superseded when the point leaves the open states.</summary>
    private static void SupersedePending(SqliteConnection c, SqliteTransaction tx, CompliancePoint p, ClaimsPrincipal u)
    {
        var a = new { p.Id, now = Db.Now(), name = u.DisplayName(), uid = u.UserId() };
        Db.Exec(c, tx, "UPDATE ComplianceUpdates SET ReviewState = 'Superseded', ReviewedAt = @now, ReviewedByName = @name, ReviewedByUserId = @uid WHERE PointId = @Id AND ReviewState = 'Pending'", a);
        Db.Exec(c, tx, "UPDATE ComplianceRevisions SET Status = 'Superseded', DecidedAt = @now, DecidedByName = @name, DecidedByUserId = @uid WHERE PointId = @Id AND Status = 'Pending'", a);
    }

    private void Finish(long pointId, string toStatus, string action, string? remarks, string? authority, long? mergeInto, long rowVersion, ClaimsPrincipal u, string what)
    {
        CompliancePoint? p = null;
        var touched = new List<long>();
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            RequireCell(c, tx, p, u);
            RequireOpen(p);
            SupersedePending(c, tx, p, u);
            Bump(c, tx, p, rowVersion, """
                Status = @to, ClosedAt = @now2, ClosedByName = @name, ClosureRemarks = @remarks, ClosureAuthority = @authority, MergedIntoId = @into,
                PendingUpdateId = NULL, PendingRevisionId = NULL, NextUpdateDue = NULL
                """, new() { ["to"] = toStatus, ["now2"] = Db.Now(), ["name"] = u.DisplayName(), ["remarks"] = remarks, ["authority"] = authority, ["into"] = mergeInto });
            History(c, tx, p.Id, p.EventId, u, action, p.Status, toStatus,
                $"{remarks}" + (authority != null ? $"; authority: {authority}" : "") + (mergeInto != null ? $"; merged into #{mergeInto}" : ""));
            touched = Officers(c, tx, p);
            notifications.Notify(c, tx, touched, Actor(u), Msg(p, action, what));
            return 0;
        });
        TouchAll(touched);
        Audit(action, p!, $"{p!.Status} → {toStatus}");
    }

    public void Close(long pointId, string? remarks, string? authority, long rowVersion, ClaimsPrincipal u) =>
        Finish(pointId, CStatus.Completed, "Closed",
            ComplianceText.Required("Closing remarks", remarks, ComplianceText.Remarks), ComplianceText.Required("Closure authority", authority, 300), null, rowVersion, u, "closed as completed");

    public void Drop(long pointId, string? reason, string? authority, long rowVersion, ClaimsPrincipal u) =>
        Finish(pointId, CStatus.Dropped, "Dropped",
            ComplianceText.Required("Reason", reason, ComplianceText.Remarks), ComplianceText.Required("Authority", authority, 300), null, rowVersion, u, "dropped");

    public void Merge(long pointId, string? targetNumber, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        var why = ComplianceText.Required("Reason", reason, ComplianceText.Remarks);
        var number = ComplianceText.Required("Merge into (point number)", targetNumber, 30);
        var src = repo.Point(pointId) ?? throw new WorkflowException("Action point not found.");
        var target = repo.PointByNumber(number);
        if (target == null || !access.CanViewPoint(target, u)) throw new WorkflowException($"Point {number} was not found.");
        if (target.Id == pointId) throw new WorkflowException("A point cannot be merged into itself.");
        if (!target.IsOpen) throw new WorkflowException("Points can only be merged into an open point.");
        if (access.IsRestricted(src.SourceType, src.IsConfidential) != access.IsRestricted(target.SourceType, target.IsConfidential))
            throw new WorkflowException("A point can only be merged into a point with the same restriction (restricted / not restricted).");
        Finish(pointId, CStatus.Merged, "Merged", why, $"Merged into {target.Number}", target.Id, rowVersion, u, $"merged into {target.Number}");
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        History(c, tx, target.Id, target.EventId, u, "MergedFrom", target.Status, target.Status, $"{src.Number} merged into this point: {why}");
        tx.Commit();
    }

    public void Reopen(long pointId, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        var why = ComplianceText.Required("Reason", reason, ComplianceText.Remarks);
        CompliancePoint? p = null;
        var touched = new List<long>();
        InTx(db, (c, tx) =>
        {
            p = Load(c, tx, pointId);
            RequireCell(c, tx, p, u);
            if (p.IsOpen) throw new WorkflowException("This point is already open.");
            var next = NextUpdateDue(p);
            Bump(c, tx, p, rowVersion, """
                Status = 'Ongoing', ClosedAt = NULL, ClosedByName = NULL, ClosureRemarks = NULL, ClosureAuthority = NULL, MergedIntoId = NULL,
                NextUpdateDue = @next, Reminded7For = NULL, Reminded1For = NULL, LastOverdueReminderOn = NULL, UpdateRemindedFor = NULL
                """, new() { ["next"] = next });
            History(c, tx, p.Id, p.EventId, u, "Reopened", p.Status, CStatus.Ongoing,
                $"{why}; previously closed {p.ClosedAt} by {p.ClosedByName}: {p.ClosureRemarks}; authority: {p.ClosureAuthority}" + (p.MergedIntoId != null ? $"; was merged into #{p.MergedIntoId}" : ""));
            touched = Officers(c, tx, p);
            notifications.Notify(c, tx, touched, Actor(u), Msg(p, "Reopened", "reopened"));
            return 0;
        });
        TouchAll(touched);
        Audit("Reopened", p!, $"{p!.Status} → Ongoing");
    }

    // ================================================================= bulk transfer

    public sealed record TransferPlan(int Primary, int Supporting, int SkippedOwn);

    private List<CompliancePoint> TransferScope(SqliteConnection c, SqliteTransaction? tx, long fromId, string scope, ClaimsPrincipal u)
    {
        var where = scope switch
        {
            "primary" => "p.PrimaryOfficerUserId = @fromId",
            "supporting" => "EXISTS (SELECT 1 FROM ComplianceSupportOfficers so WHERE so.PointId = p.Id AND so.UserId = @fromId)",
            _ => "(p.PrimaryOfficerUserId = @fromId OR EXISTS (SELECT 1 FROM ComplianceSupportOfficers so WHERE so.PointId = p.Id AND so.UserId = @fromId))",
        };
        return Db.Query<CompliancePoint>(c, tx, ComplianceRepository.PointSelect + $" WHERE p.Status IN {CStatus.OpenSql} AND {where} AND {access.ManagedVisibleSql(u)}", new { fromId, uid = u.UserId() });
    }

    public TransferPlan PlanTransfer(long fromId, string scope, ClaimsPrincipal u)
    {
        using var c = db.Open();
        var pts = TransferScope(c, null, fromId, scope, u);
        var own = pts.Count(p => ComplianceAccess.IsOfficer(c, null!, p.Id, u.UserId()));
        return new TransferPlan(pts.Count(p => p.PrimaryOfficerUserId == fromId), pts.Count(p => p.PrimaryOfficerUserId != fromId), own);
    }

    public int Transfer(long fromId, long toId, string? scope, ClaimsPrincipal u)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only the Compliance Cell can transfer action points.");
        if (fromId == toId) throw new WorkflowException("Choose two different officers.");
        if (toId == u.UserId()) throw new WorkflowException("You cannot transfer action points to yourself.");
        scope = scope is "primary" or "supporting" ? scope : "both";
        var moved = 0;
        var touched = new List<long> { fromId, toId };
        var numbers = new List<string>();
        InTx(db, (c, tx) =>
        {
            if (Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM Users WHERE Id = @toId AND IsActive = 1", new { toId }) == 0) throw new WorkflowException("The new officer must be an active user.");
            foreach (var p in TransferScope(c, tx, fromId, scope, u))
            {
                if (ComplianceAccess.IsOfficer(c, tx, p.Id, u.UserId())) continue;      // own points need another CA
                var detail = new List<string>();
                if (p.PrimaryOfficerUserId == fromId)
                {
                    Db.Exec(c, tx, "UPDATE CompliancePoints SET PrimaryOfficerUserId = @toId, Reminded7For = NULL, Reminded1For = NULL, LastOverdueReminderOn = NULL, UpdateRemindedFor = NULL, RowVersion = RowVersion + 1, UpdatedAt = @now WHERE Id = @Id",
                        new { toId, p.Id, now = Db.Now() });
                    Db.Exec(c, tx, "DELETE FROM ComplianceSupportOfficers WHERE PointId = @Id AND UserId = @toId", new { p.Id, toId });
                    detail.Add($"Primary officer #{fromId} → #{toId}");
                }
                if (scope != "primary" && Db.Exec(c, tx, "DELETE FROM ComplianceSupportOfficers WHERE PointId = @Id AND UserId = @fromId", new { p.Id, fromId }) > 0)
                {
                    if (p.PrimaryOfficerUserId != toId && p.PrimaryOfficerUserId != fromId)
                        Db.Exec(c, tx, "INSERT OR IGNORE INTO ComplianceSupportOfficers (PointId, UserId) VALUES (@Id, @toId)", new { p.Id, toId });
                    detail.Add($"Supporting officer #{fromId} → #{toId}");
                }
                if (detail.Count == 0) continue;
                History(c, tx, p.Id, p.EventId, u, "Transferred", p.Status, p.Status, string.Join("; ", detail));
                touched.AddRange(Officers(c, tx, p));
                numbers.Add(p.Number);
                moved++;
            }
            if (moved > 0)
            {
                notifications.Notify(c, tx, new[] { toId }, Actor(u), new NotificationMessage("Compliance.Assigned", $"{moved} action point(s) transferred to you", null, "/Compliance/Mine"));
                notifications.Notify(c, tx, new[] { fromId }, Actor(u), new NotificationMessage("Compliance.Reassigned", $"{moved} action point(s) transferred to another officer", null, "/Compliance/Mine"));
            }
            return 0;
        });
        TouchAll(touched.Distinct());
        audit.Log("Compliance.Transferred", "User", fromId, $"{moved} open points from user #{fromId} to user #{toId} ({scope})");
        return moved;
    }

    // ================================================================= grants

    public void Grant(long userId, string role, ClaimsPrincipal u)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only a Compliance Admin can grant compliance access.");
        GrantCore(userId, role, u.DisplayName(), u.UserId());
    }

    /// <summary>Grants a role; used by the Members page and by the server command <c>--grant-compliance-admin</c>.</summary>
    public void GrantCore(long userId, string role, string actorName, long actorId = 0)
    {
        if (!ComplianceGrant.Labels.ContainsKey(role)) throw new WorkflowException("Choose Compliance Admin or Compliance Viewer.");
        if (db.Scalar<long>("SELECT COUNT(*) FROM Users WHERE Id = @userId AND IsActive = 1", new { userId }) == 0) throw new WorkflowException("Choose an active user.");
        var admins = access.ActiveAdminIds();
        InTx(db, (c, tx) =>
        {
            var now = Db.Now();
            Db.Exec(c, tx, """
                INSERT INTO ComplianceMembers (UserId, Role, GrantedByName, GrantedAt) VALUES (@userId, @role, @actorName, @now)
                ON CONFLICT(UserId) DO UPDATE SET Role = excluded.Role, GrantedByName = excluded.GrantedByName, GrantedAt = excluded.GrantedAt
                """, new { userId, role, actorName, now });
            Db.Exec(c, tx, "INSERT INTO ComplianceMemberHistory (UserId, Action, Role, ActorName, At) VALUES (@userId, 'Granted', @role, @actorName, @now)", new { userId, role, actorName, now });
            notifications.Notify(c, tx, admins.Append(userId), new NotificationActor(actorId, actorName),
                new NotificationMessage("Compliance.AccessChanged", $"Compliance access: {ComplianceGrant.Labels[role]} granted to user #{userId}", $"by {actorName}", "/Compliance/Members"));
            return 0;
        });
        access.Invalidate();
        tracker.Touch(ChangeTracker.ComplianceQueue);
        tracker.TouchUser(userId);
        audit.Log("Compliance.AccessGranted", "User", userId, $"user #{userId}: {role}", actorName);
    }

    public void Revoke(long userId, ClaimsPrincipal u)
    {
        if (!access.IsAdmin(u)) throw new WorkflowException("Only a Compliance Admin can revoke compliance access.");
        if (userId == u.UserId()) throw new WorkflowException("You cannot revoke your own access; ask another Compliance Admin.");
        var role = access.GrantOf(userId) ?? db.Scalar<string>("SELECT Role FROM ComplianceMembers WHERE UserId = @userId", new { userId })
                   ?? throw new WorkflowException("This user has no compliance access.");
        var admins = access.ActiveAdminIds();
        InTx(db, (c, tx) =>
        {
            var now = Db.Now();
            Db.Exec(c, tx, "DELETE FROM ComplianceMembers WHERE UserId = @userId", new { userId });
            Db.Exec(c, tx, "INSERT INTO ComplianceMemberHistory (UserId, Action, Role, ActorName, At) VALUES (@userId, 'Revoked', @role, @name, @now)", new { userId, role, name = u.DisplayName(), now });
            notifications.Notify(c, tx, admins.Append(userId), Actor(u),
                new NotificationMessage("Compliance.AccessChanged", $"Compliance access revoked for user #{userId}", $"by {u.DisplayName()}", "/Compliance/Members"));
            return 0;
        });
        access.Invalidate();
        tracker.Touch(ChangeTracker.ComplianceQueue);
        tracker.TouchUser(userId);
        audit.Log("Compliance.AccessRevoked", "User", userId, $"user #{userId}: {role}");
    }

    /// <summary>Tells the Cell when an intranet administrator sets a new password for a compliance member (DD-07 §5 rule 5).</summary>
    public void NotifyMemberPasswordReset(long userId, ClaimsPrincipal admin)
    {
        if (!o.Enabled || access.GrantOf(userId) == null) return;
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        notifications.Notify(c, tx, access.ActiveAdminIds().Append(userId), Actor(admin),
            new NotificationMessage("Compliance.AccessChanged", $"Password of compliance member #{userId} was reset by an administrator", $"by {admin.DisplayName()}", "/Compliance/Members"));
        tx.Commit();
        audit.Log("Compliance.MemberPasswordReset", "User", userId, $"user #{userId}; Compliance Cell notified");
    }

    /// <summary>The Auditor role reads non-restricted compliance data, so the Cell is told whenever someone gains it (DD-07 R-6).</summary>
    public void NotifyAuditorGranted(long userId, ClaimsPrincipal admin)
    {
        if (!o.Enabled) return;
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        notifications.Notify(c, tx, access.ActiveAdminIds(), Actor(admin),
            new NotificationMessage("Compliance.AccessChanged", $"User #{userId} was given the Auditor role (reads non-restricted compliance data)", $"by {admin.DisplayName()}", "/Compliance/Members"));
        tx.Commit();
    }

    // ================================================================= daily reminders

    private sealed class Due
    {
        public long Id { get; set; }
        public long PrimaryOfficerUserId { get; set; }
        public string CurrentDueDate { get; set; } = "";
        public string? NextUpdateDue { get; set; }
        public long? PendingUpdateId { get; set; }
        public string? Reminded7For { get; set; }
        public string? Reminded1For { get; set; }
        public string? LastOverdueReminderOn { get; set; }
        public string? UpdateRemindedFor { get; set; }
    }

    /// <summary>Runs the daily reminders once per office day, at or after <c>ReminderHour</c> (called by ComplianceScheduler).</summary>
    public List<string>? RunDailyIfDue()
    {
        if (!o.Enabled || fmt.LocalNow.Hour < o.ReminderHour) return null;
        var today = Today;
        if (db.Scalar<string>("SELECT Value FROM ComplianceState WHERE Key = 'LastReminderRun'") == today) return null;
        var result = DailyReminders(fmt.Today);
        db.Exec("INSERT INTO ComplianceState (Key, Value) VALUES ('LastReminderRun', @today) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value", new { today });
        return result;
    }

    /// <summary>
    /// Reminders for open points of active officers (DD-07 §8): due soon, due tomorrow/today, overdue (repeating), update due.
    /// One aggregated notification per officer, and a digest to the Cell. Returns counts only.
    /// </summary>
    public List<string> DailyReminders(DateOnly today)
    {
        if (!o.Enabled) return new();
        string D(int add) => today.AddDays(add).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var t = D(0);
        var perOfficer = new Dictionary<long, (int overdue, int soon, int tomorrow, int update)>();
        int sent = 0, digests = 0;
        InTx(db, (c, tx) =>
        {
            var due = Db.Query<Due>(c, tx, $"""
                SELECT p.Id, p.PrimaryOfficerUserId, p.CurrentDueDate, p.NextUpdateDue, p.PendingUpdateId, p.Reminded7For, p.Reminded1For, p.LastOverdueReminderOn, p.UpdateRemindedFor
                FROM CompliancePoints p JOIN Users u ON u.Id = p.PrimaryOfficerUserId
                WHERE p.Status IN {CStatus.OpenSql} AND u.IsActive = 1 AND (p.CurrentDueDate <= @soon OR p.NextUpdateDue <= @t)
                """, new { soon = D(o.DueSoonDays), t });
            foreach (var p in due)
            {
                var (ov, so, tm, up) = perOfficer.GetValueOrDefault(p.PrimaryOfficerUserId);
                var any = false;
                if (string.CompareOrdinal(p.CurrentDueDate, t) < 0)
                {
                    if (p.LastOverdueReminderOn == null || string.CompareOrdinal(p.LastOverdueReminderOn, D(-o.OverdueRepeatDays)) <= 0)
                    { ov++; any = true; Db.Exec(c, tx, "UPDATE CompliancePoints SET LastOverdueReminderOn = @t WHERE Id = @Id", new { t, p.Id }); }
                }
                else if (string.CompareOrdinal(p.CurrentDueDate, D(1)) <= 0)
                {
                    if (p.Reminded1For != p.CurrentDueDate)
                    { tm++; any = true; Db.Exec(c, tx, "UPDATE CompliancePoints SET Reminded1For = CurrentDueDate WHERE Id = @Id", new { p.Id }); }
                }
                else if (string.CompareOrdinal(p.CurrentDueDate, D(o.DueSoonDays)) <= 0)
                {
                    if (p.Reminded7For != p.CurrentDueDate)
                    { so++; any = true; Db.Exec(c, tx, "UPDATE CompliancePoints SET Reminded7For = CurrentDueDate WHERE Id = @Id", new { p.Id }); }
                }
                if (p.NextUpdateDue != null && string.CompareOrdinal(p.NextUpdateDue, t) <= 0 && p.PendingUpdateId == null && p.UpdateRemindedFor != p.NextUpdateDue)
                { up++; any = true; Db.Exec(c, tx, "UPDATE CompliancePoints SET UpdateRemindedFor = NextUpdateDue WHERE Id = @Id", new { p.Id }); }
                if (any) perOfficer[p.PrimaryOfficerUserId] = (ov, so, tm, up);
            }
            var system = new NotificationActor(0, "NABCONS Intranet");
            foreach (var (uid, (ov, so, tm, up)) in perOfficer)
            {
                var parts = new List<string>();
                if (ov > 0) parts.Add($"{ov} overdue");
                if (tm > 0) parts.Add($"{tm} due today or tomorrow");
                if (so > 0) parts.Add($"{so} due within {o.DueSoonDays} days");
                if (up > 0) parts.Add($"{up} periodic update(s) due");
                sent += notifications.Notify(c, tx, new[] { uid }, system, new NotificationMessage("Compliance.Reminder",
                    "Compliance reminder: " + string.Join(", ", parts), "Your action points need attention.", "/Compliance/Mine"));
            }
            var awaiting = Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM CompliancePoints WHERE PendingUpdateId IS NOT NULL OR PendingRevisionId IS NOT NULL OR (Status = 'ProposedClosure' AND PendingUpdateId IS NULL)");
            var overdue = Db.Scalar<long>(c, tx, $"SELECT COUNT(*) FROM CompliancePoints WHERE Status IN {CStatus.OpenSql} AND CurrentDueDate < @t", new { t });
            if (awaiting + overdue > 0)
                digests = notifications.Notify(c, tx, access.ActiveAdminIds(), system, new NotificationMessage("Compliance.Digest",
                    $"Compliance digest: {awaiting} awaiting the Cell, {overdue} overdue", null, "/Compliance/Review"));
            return 0;
        });
        foreach (var uid in perOfficer.Keys) tracker.TouchUser(uid);
        tracker.Touch(ChangeTracker.ComplianceQueue);
        var result = new List<string>();
        if (sent > 0 || digests > 0) result.Add($"Compliance reminders: {sent} officer(s), digest to {digests} Cell member(s)");
        return result;
    }
}

