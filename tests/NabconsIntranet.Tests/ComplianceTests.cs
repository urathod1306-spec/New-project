using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Tests;

/// <summary>Compliance management (release 1.6.0), test plan of DD-07 §13.</summary>
public class ComplianceTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private ComplianceWorkflow W => _app.Get<ComplianceWorkflow>();
    private ComplianceRepository R => _app.Get<ComplianceRepository>();
    private AtrBuilder Atr => _app.Get<AtrBuilder>();
    private Db Db => _app.Get<Db>();
    private ClaimsPrincipal P(string u) => _app.Principal(u);
    private string D(int days) => _app.Get<Fmt>().Today.AddDays(days).ToString("yyyy-MM-dd");
    private long Rv(long id) => R.Point(id)!.RowVersion;
    private long Forum(string name) => _app.Get<MasterRepository>().List(MasterTypes.ComplianceForum).First(m => m.Name == name).Id;

    private long Event(string source = ComplianceSource.StatutoryAudit, string forum = "Statutory Auditor", bool confidential = false, int heldDaysAgo = 20) =>
        W.CreateEvent(new ComplianceEventInput { SourceType = source, ForumId = Forum(forum), Title = "Test event " + Guid.NewGuid().ToString("N")[..6], HeldOn = D(-heldDaysAgo), IsConfidential = confidential, ReportingCycle = ComplianceCycle.Quarterly }, P("cs"));

    private long Point(long ev, string officer = "staff", int dueInDays = 30, string text = "Reconcile the fixed asset register with the books") =>
        W.CreatePoint(ev, new CompliancePointInput { RefNo = "Para 1", PointText = text, PrimaryOfficerUserId = _app.User(officer).Id, DueDate = D(dueInDays) }, P("cs"));

    // ------------------------------------------------------------------ schema

    [Fact]
    public void Migration_7_protects_records_with_triggers_and_checks()
    {
        Assert.True(Schema.Version >= 7);
        Assert.Empty(Db.Initialize());                                                          // applied once; nothing to do again
        var id = Point(Event());
        Assert.Throws<SqliteException>(() => Db.Exec("DELETE FROM CompliancePoints WHERE Id = @id", new { id }));
        Assert.Throws<SqliteException>(() => Db.Exec("UPDATE CompliancePoints SET OriginalDueDate = '2030-01-01' WHERE Id = @id", new { id }));
        Assert.Throws<SqliteException>(() => Db.Exec("UPDATE CompliancePoints SET Number = 'X' WHERE Id = @id", new { id }));
        Db.Exec("UPDATE CompliancePoints SET OriginalDueDate = OriginalDueDate WHERE Id = @id", new { id });     // unchanged value is fine
        Assert.Throws<SqliteException>(() => Db.Exec("UPDATE CompliancePoints SET PointText = @t WHERE Id = @id", new { id, t = new string('x', 4001) }));
        Assert.Throws<SqliteException>(() => Db.Exec("UPDATE ComplianceHistory SET Detail = 'x'"));
        Assert.Throws<SqliteException>(() => Db.Exec("DELETE FROM ComplianceHistory"));
        Assert.Throws<SqliteException>(() => Db.Exec("DELETE FROM ComplianceEvents"));
    }

    [Fact]
    public void Numbers_restart_each_financial_year()
    {
        var ev = Event();
        var march = W.CreatePoint(ev, new CompliancePointInput { PointText = "A", PrimaryOfficerUserId = _app.User("staff").Id, DueDate = D(10) }, P("cs"), new DateOnly(2026, 3, 31));
        var april = W.CreatePoint(ev, new CompliancePointInput { PointText = "B", PrimaryOfficerUserId = _app.User("staff").Id, DueDate = D(10) }, P("cs"), new DateOnly(2025, 3, 31));
        Assert.Equal("CMP/2025-26/00001", R.Point(march)!.Number);
        Assert.Equal("CMP/2024-25/00001", R.Point(april)!.Number);
        Assert.StartsWith("CMP/2026-27/", R.Point(Point(ev))!.Number);
    }

    // ------------------------------------------------------------------ lifecycle

    [Fact]
    public void Full_lifecycle_keeps_the_officer_text_and_needs_a_closure_authority()
    {
        var id = Point(Event());
        var staff = P("staff");
        var cs = P("cs");
        var u1 = W.SubmitUpdate(id, D(0), "Work started.", CStatus.Ongoing, null, Rv(id), staff);
        Assert.Throws<WorkflowException>(() => W.SubmitUpdate(id, D(0), "Again", CStatus.Ongoing, null, Rv(id), staff));      // one pending at a time
        W.ReturnUpdate(id, u1, "Give the number of assets reconciled.", Rv(id), cs);
        Assert.NotNull(R.Point(id)!.LastReturnedAt);
        var u2 = W.SubmitUpdate(id, D(0), "Reconciled 412 of 430 assets.", CStatus.ProposedClosure, "File F&A/FA/7", Rv(id), staff);
        Assert.Null(R.Point(id)!.LastReturnedAt);
        W.AcceptUpdate(id, u2, "Fixed asset register reconciled (412 of 430; balance written off with approval).", Rv(id), cs);
        var p = R.Point(id)!;
        Assert.Equal(CStatus.ProposedClosure, p.Status);
        Assert.StartsWith("Fixed asset register reconciled", p.AtrText);
        Assert.Equal("Reconciled 412 of 430 assets.", R.Update(u2)!.UpdateText);                                       // officer text unchanged
        Assert.Throws<SqliteException>(() => Db.Exec("UPDATE ComplianceUpdates SET UpdateText = 'changed' WHERE Id = @u2", new { u2 }));
        Assert.Throws<SqliteException>(() => Db.Exec("UPDATE ComplianceUpdates SET ReviewRemarks = 'later' WHERE Id = @u2", new { u2 }));
        Assert.Throws<WorkflowException>(() => W.Close(id, "Done", null, Rv(id), cs));                                 // authority required
        Assert.Throws<WorkflowException>(() => W.Close(id, "Done", "Board", Rv(id), staff));                            // officers cannot close
        W.Close(id, "Done", "Taken on record by the Board, 99th meeting", Rv(id), cs);
        Assert.Equal(CStatus.Completed, R.Point(id)!.Status);
        Assert.Throws<WorkflowException>(() => W.SubmitUpdate(id, D(0), "More", CStatus.Ongoing, null, Rv(id), staff));
        W.Reopen(id, "Board asked for the balance assets", Rv(id), cs);
        p = R.Point(id)!;
        Assert.Equal(CStatus.Ongoing, p.Status);
        Assert.Null(p.ClosureAuthority);
        Assert.Contains(R.History(id), h => h.Action == "Reopened" && h.Detail!.Contains("Taken on record"));
    }

    [Fact]
    public void Closing_supersedes_pending_items_and_withdraw_works()
    {
        var id = Point(Event());
        var u = W.SubmitUpdate(id, D(0), "Started", CStatus.Ongoing, null, Rv(id), P("staff"));
        W.WithdrawUpdate(id, u, P("staff"));
        Assert.Equal("Withdrawn", R.Update(u)!.ReviewState);
        var u2 = W.SubmitUpdate(id, D(0), "Started again", CStatus.Ongoing, null, Rv(id), P("staff"));
        var rev = W.RequestRevision(id, D(60), "Waiting for the auditor", Rv(id), P("staff"));
        W.Drop(id, "No longer relevant", "Dropped by the auditor in the Q3 report", Rv(id), P("cs"));
        Assert.Equal("Superseded", R.Update(u2)!.ReviewState);
        Assert.Equal("Superseded", R.Revision(rev)!.Status);
        var p = R.Point(id)!;
        Assert.Null(p.PendingUpdateId);
        Assert.Null(p.PendingRevisionId);
    }

    [Fact]
    public void Revisions_never_change_the_original_due_date()
    {
        var id = Point(Event(), dueInDays: 10);
        var original = R.Point(id)!.OriginalDueDate;
        var rev = W.RequestRevision(id, D(40), "Auditor visit postponed", Rv(id), P("staff"));
        Assert.Throws<WorkflowException>(() => W.RequestRevision(id, D(50), "again", Rv(id), P("staff")));
        Assert.Throws<WorkflowException>(() => W.RequestRevision(id, D(50), "not mine", Rv(id), P("approver1")));
        W.DecideRevision(id, rev, true, null, Rv(id), P("cs"));
        var p = R.Point(id)!;
        Assert.Equal(D(40), p.CurrentDueDate);
        Assert.Equal(original, p.OriginalDueDate);
        var rev2 = W.RequestRevision(id, D(70), "Further delay", Rv(id), P("staff"));
        W.ReviseDirect(id, D(55), "Cell decision", Rv(id), P("cs"));
        Assert.Equal("Superseded", R.Revision(rev2)!.Status);
        Assert.Equal(D(55), R.Point(id)!.CurrentDueDate);
    }

    [Fact]
    public void Stale_row_version_is_refused()
    {
        var id = Point(Event());
        var stale = Rv(id);
        W.SubmitUpdate(id, D(0), "x", CStatus.Ongoing, null, stale, P("staff"));
        var ex = Assert.Throws<WorkflowException>(() => W.Close(id, "r", "a", stale, P("cs")));
        Assert.Contains("changed by someone else", ex.Message);
    }

    [Fact]
    public void Cell_members_never_act_on_their_own_points()
    {
        W.GrantCore(_app.User("approver1").Id, ComplianceGrant.Admin, "test");
        var ev = Event();
        Assert.Throws<WorkflowException>(() => Point(ev, "cs"));                                // cannot assign themselves
        var id = W.CreatePoint(ev, new CompliancePointInput { PointText = "Own", PrimaryOfficerUserId = _app.User("cs").Id, DueDate = D(10) }, P("approver1"));
        var u = W.SubmitUpdate(id, D(0), "Done", CStatus.Ongoing, null, Rv(id), P("cs"));
        Assert.Throws<WorkflowException>(() => W.AcceptUpdate(id, u, null, Rv(id), P("cs")));
        Assert.Throws<WorkflowException>(() => W.Close(id, "r", "a", Rv(id), P("cs")));
        W.AcceptUpdate(id, u, null, Rv(id), P("approver1"));                                   // another CA may
    }

    [Fact]
    public void Merge_needs_an_open_point_with_the_same_restriction()
    {
        var a = Point(Event());
        var b = Point(Event());
        var restricted = Point(Event(ComplianceSource.Board, "Board of Directors"));
        Assert.Throws<WorkflowException>(() => W.Merge(a, R.Point(restricted)!.Number, "dup", Rv(a), P("cs")));
        Assert.Throws<WorkflowException>(() => W.Merge(a, R.Point(a)!.Number, "self", Rv(a), P("cs")));
        W.Merge(a, R.Point(b)!.Number, "Same observation", Rv(a), P("cs"));
        Assert.Equal(CStatus.Merged, R.Point(a)!.Status);
        Assert.Equal(b, R.Point(a)!.MergedIntoId);
        Assert.Throws<WorkflowException>(() => W.Merge(b, R.Point(a)!.Number, "back", Rv(b), P("cs")));     // target not open
    }

    // ------------------------------------------------------------------ permissions

    [Fact]
    public async Task Visibility_follows_the_permissions_matrix()
    {
        var open = Point(Event(), "approver1");
        var board = Point(Event(ComplianceSource.Board, "Board of Directors"), "approver1");
        var confidential = Point(Event(confidential: true), "approver1");
        async Task<HttpStatusCode> Get(string user, string url) => (await (await _app.SignIn(user)).GetAsync(url)).StatusCode;

        Assert.Equal(HttpStatusCode.OK, await Get("approver1", $"/Compliance/Point/{board}"));          // own point
        Assert.Equal(HttpStatusCode.NotFound, await Get("staff", $"/Compliance/Point/{open}"));         // someone else's
        Assert.Equal(HttpStatusCode.NotFound, await Get("admin", $"/Compliance/Point/{open}"));         // IT administrator: no grant
        Assert.Equal(HttpStatusCode.NotFound, await Get("admin", "/Compliance/Points"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("admin", "/Compliance/Members"));
        Assert.Equal(HttpStatusCode.OK, await Get("auditor", $"/Compliance/Point/{open}"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("auditor", $"/Compliance/Point/{board}"));       // restricted
        Assert.Equal(HttpStatusCode.NotFound, await Get("auditor", $"/Compliance/Point/{confidential}"));
        Assert.Equal(HttpStatusCode.OK, await Get("coo", $"/Compliance/Point/{board}"));                // viewer
        Assert.Equal(HttpStatusCode.NotFound, await Get("coo", "/Compliance/Review"));                   // viewer is not the Cell

        // restricted rows never reach the auditor's counts, lists or ATR choices
        var auditor = P("auditor");
        var all = R.Points(P("coo"), new CompliancePointFilter { Status = "all" }).Items.Select(p => p.Id).ToList();
        var seen = R.Points(auditor, new CompliancePointFilter { Status = "all" }).Items.Select(p => p.Id).ToList();
        Assert.Contains(board, all);
        Assert.DoesNotContain(board, seen);
        Assert.DoesNotContain(confidential, seen);
        Assert.True(R.Dashboard(auditor).Open < R.Dashboard(P("coo")).Open);
        Assert.Throws<WorkflowException>(() => Atr.Build(new AtrOptions { Template = AtrTemplate.BoardAtr, EventIds = { R.Point(board)!.EventId }, AsOn = D(0) }, auditor));
        // notifications and audit details carry numbers, never the text
        var texts = Db.Strings("SELECT Title || ' ' || COALESCE(Message, '') FROM Notifications WHERE Type LIKE 'Compliance.%'")
            .Concat(Db.Strings("SELECT COALESCE(Details, '') FROM AuditLog WHERE Action LIKE 'Compliance.%'"));
        Assert.DoesNotContain(texts, t => t.Contains("fixed asset register", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Db.Strings("SELECT Message FROM Notifications WHERE Type = 'Compliance.Assigned' AND EntityId = @board", new { board }), m => m.StartsWith("Restricted"));
    }

    [Fact]
    public async Task Auditor_can_be_allowed_to_read_restricted_sources()
    {
        using var app = new TestApp(("Compliance__AuditorSeesRestricted", "true"));
        var w = app.Get<ComplianceWorkflow>();
        var ev = w.CreateEvent(new ComplianceEventInput { SourceType = ComplianceSource.Board, ForumId = app.Get<MasterRepository>().List(MasterTypes.ComplianceForum).First().Id, Title = "Board", HeldOn = "2026-09-01" }, app.Principal("cs"));
        var id = w.CreatePoint(ev, new CompliancePointInput { PointText = "x", PrimaryOfficerUserId = app.User("staff").Id, DueDate = "2026-12-31" }, app.Principal("cs"));
        Assert.Equal(HttpStatusCode.OK, (await (await app.SignIn("auditor")).GetAsync($"/Compliance/Point/{id}")).StatusCode);
    }

    [Fact]
    public void Only_the_Cell_grants_access_and_no_one_revokes_themselves()
    {
        Assert.Throws<WorkflowException>(() => W.Grant(_app.User("admin").Id, ComplianceGrant.Admin, P("admin")));   // IT admin cannot
        Assert.Throws<WorkflowException>(() => W.Grant(_app.User("staff").Id, ComplianceGrant.Viewer, P("coo")));     // viewer cannot
        Assert.Throws<WorkflowException>(() => W.Revoke(_app.User("cs").Id, P("cs")));
        W.Grant(_app.User("approver2").Id, ComplianceGrant.Viewer, P("cs"));
        Assert.Equal(ComplianceGrant.Viewer, _app.Get<ComplianceAccess>().GrantOf(_app.User("approver2").Id));
        W.Revoke(_app.User("approver2").Id, P("cs"));
        Assert.Null(_app.Get<ComplianceAccess>().GrantOf(_app.User("approver2").Id));
        Assert.Throws<SqliteException>(() => Db.Exec("DELETE FROM ComplianceMemberHistory"));
        Assert.Equal(2, Db.Scalar<long>("SELECT COUNT(*) FROM AuditLog WHERE Action IN ('Compliance.AccessGranted','Compliance.AccessRevoked') AND EntityId = @id", new { id = _app.User("approver2").Id.ToString() }));
    }

    // ------------------------------------------------------------------ ATR and snapshots

    private (long ev1, long ev2) BoardPack()
    {
        var older = W.CreateEvent(new ComplianceEventInput { SourceType = ComplianceSource.AuditCommittee, ForumId = Forum("Audit Committee"), EventNo = "40th", Title = "Audit Committee", HeldOn = D(-120) }, P("cs"));
        var newer = W.CreateEvent(new ComplianceEventInput { SourceType = ComplianceSource.AuditCommittee, ForumId = Forum("Audit Committee"), EventNo = "41st", Title = "Audit Committee", HeldOn = D(-30) }, P("cs"));
        var a = W.CreatePoint(older, new CompliancePointInput { RefNo = "Agenda 3", PointText = "Review TDS defaults", PrimaryOfficerUserId = _app.User("approver2").Id, DueDate = D(5),
            InitialAtrText = "Defaults identified for FY 2024-25.", InitialAsOn = D(-60) }, P("cs"));
        W.CreatePoint(newer, new CompliancePointInput { RefNo = "Agenda 2", RefSubject = "Internal audit plan", PointText = "Approve the internal audit plan", PrimaryOfficerUserId = _app.User("staff").Id, DueDate = D(20) }, P("cs"));
        _ = a;
        return (older, newer);
    }

    [Fact]
    public void Final_ATR_is_immutable_verifiable_and_regenerates_identically()
    {
        var (older, newer) = BoardPack();
        var cs = P("cs");
        var opt = new AtrOptions { Template = AtrTemplate.BoardAtr, EventIds = { older, newer }, AsOn = D(0) };
        var b = Atr.Build(opt, cs);
        Assert.Equal(2, b.Header.Sections.Count(s => !s.IsSummary));
        Assert.StartsWith("Annexure I — 41st", b.Header.Sections[0].Heading);
        Assert.Throws<WorkflowException>(() => Atr.Finalise(opt, "0000", "Audit Committee", D(0), cs));          // stale or wrong preview hash
        Assert.Throws<WorkflowException>(() => Atr.Finalise(opt, b.Hash, "Audit Committee", D(0), P("coo")));     // viewers cannot
        var id = Atr.Finalise(opt, b.Hash, "Audit Committee", D(0), cs);
        var s = Atr.Snapshot(id)!;
        Assert.Equal(b.Hash, s.Sha256);
        Assert.True(Atr.Verify(s));
        Assert.Contains(Db.Strings("SELECT Details FROM AuditLog WHERE Action = 'Compliance.AtrFinalised'"), d => d.Contains(b.Hash));
        Assert.Throws<SqliteException>(() => Db.Exec("UPDATE ComplianceSnapshotItems SET Cells = '[]' WHERE SnapshotId = @id", new { id }));
        Assert.Throws<SqliteException>(() => Db.Exec("DELETE FROM ComplianceSnapshots WHERE Id = @id", new { id }));

        foreach (var format in ReportFormat.All)
        {
            byte[] Render(ReportDocument d) { using var ms = new MemoryStream(); ReportFormat.Write(d, format, ms); return ms.ToArray(); }
            var regen1 = Render(Atr.Regenerate(s));
            var regen2 = Render(Atr.Regenerate(Atr.Snapshot(id)!));
            Assert.Equal(regen1, regen2);
            // the live document at the time it was marked final, with the final footer, gives the same bytes
            var live = AtrBuilder.ToDocument(b.Header, b.Items, Atr.FinalFooter(s), AtrBuilder.CreatedAt(s), $"ATR-snapshot-{s.Id}");
            Assert.Equal(regen1, Render(live));
        }
    }

    [Fact]
    public void Next_ATR_shows_what_was_presented_last_time()
    {
        var (older, newer) = BoardPack();
        var cs = P("cs");
        var opt = new AtrOptions { Template = AtrTemplate.BoardAtr, EventIds = { older, newer }, AsOn = D(0) };
        var first = Atr.Build(opt, cs);
        Atr.Finalise(opt, first.Hash, "Audit Committee", D(0), cs);
        var pointId = R.EventPoints(older, cs).Single().Id;
        var u = W.SubmitUpdate(pointId, D(0), "Defaults rectified and revised returns filed.", CStatus.ProposedClosure, null, Rv(pointId), P("approver2"));
        W.AcceptUpdate(pointId, u, null, Rv(pointId), cs);
        var next = Atr.Build(new AtrOptions { Template = AtrTemplate.BoardAtr, EventIds = { older, newer }, AsOn = D(0) }, cs);
        var row = next.Items.Single(i => i.PointId == pointId);
        Assert.Equal("Defaults identified for FY 2024-25.", row.Cells[2]);                          // presented last time
        Assert.Equal("Defaults rectified and revised returns filed. [Completed]", row.Cells[3]);
        // a void snapshot is ignored
        var sid = Db.Scalar<long>("SELECT MAX(Id) FROM ComplianceSnapshots");
        Atr.Void(sid, "Marked final by mistake", cs);
        Assert.Equal("—", Atr.Build(new AtrOptions { Template = AtrTemplate.BoardAtr, EventIds = { older, newer }, AsOn = D(0) }, cs).Items.Single(i => i.PointId == pointId).Cells[2]);
    }

    [Fact]
    public void As_on_date_cannot_be_earlier_than_the_latest_change_and_packs_need_one_forum()
    {
        var (older, newer) = BoardPack();
        var ex = Assert.Throws<WorkflowException>(() => Atr.Build(new AtrOptions { Template = AtrTemplate.BoardAtr, EventIds = { older, newer }, AsOn = D(-90) }, P("cs")));
        Assert.Contains("cannot be earlier", ex.Message);
        var other = Event(ComplianceSource.Board, "Board of Directors");
        Assert.Throws<WorkflowException>(() => Atr.Build(new AtrOptions { Template = AtrTemplate.BoardAtr, EventIds = { older, other }, AsOn = D(0) }, P("cs")));
        // observation-reply may mix forums
        Assert.NotNull(Atr.Build(new AtrOptions { Template = AtrTemplate.ObservationReply, EventIds = { older, other }, AsOn = D(0) }, P("cs")));
    }

    [Fact]
    public void Internal_audit_sheet_marks_new_and_old_observations()
    {
        var q1 = W.CreateEvent(new ComplianceEventInput { SourceType = ComplianceSource.InternalAudit, ForumId = Forum("Internal Auditor"), Title = "IA Q1", PeriodTo = D(-100) }, P("cs"));
        var q2 = W.CreateEvent(new ComplianceEventInput { SourceType = ComplianceSource.InternalAudit, ForumId = Forum("Internal Auditor"), Title = "IA Q2", PeriodTo = D(-10) }, P("cs"));
        var old = W.CreatePoint(q1, new CompliancePointInput { PointText = "Old observation", PrimaryOfficerUserId = _app.User("approver2").Id, OfficerCode = "AC(AA)", DueDate = D(5) }, P("cs"));
        W.CreatePoint(q2, new CompliancePointInput { PointText = "New observation", PrimaryOfficerUserId = _app.User("approver2").Id, OfficerCode = "AC(AA)", DueDate = D(25) }, P("cs"));
        W.CreatePoint(q2, new CompliancePointInput { PointText = "Repeated observation", PrimaryOfficerUserId = _app.User("approver2").Id, DueDate = D(25), CarriedFromNumber = R.Point(old)!.Number }, P("cs"));
        var b = Atr.Build(new AtrOptions { Template = AtrTemplate.InternalAudit, EventIds = { q1, q2 }, AsOn = D(0) }, P("cs"));
        Assert.Equal(new[] { "Old", "New", "Old" }, b.Items.Select(i => i.Cells[5]).ToArray());
        Assert.Equal("AC(AA)", b.Items[0].Cells[2]);
        Assert.Equal("Anita Deshmukh", b.Items[2].Cells[2]);                                       // no code: name
    }

    // ------------------------------------------------------------------ import and the no-files rule

    private static MultipartFormDataContent Multipart(string token, params (string name, string value)[] fields)
    {
        var m = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (n, v) in fields) m.Add(new StringContent(v), n);
        return m;
    }

    [Fact]
    public async Task Csv_import_is_previewed_then_imported_all_or_nothing_and_never_twice()
    {
        var ev = Event();
        var c = await _app.SignIn("cs");
        var url = $"/Compliance/Import?eventId={ev}";
        var csv = "RefNo,ActionPoint,PrimaryOfficer,SupportingOfficers,DueDate,Category\r\nPara 1,First observation,staff,approver1,31.12.2026,Taxation\r\nPara 2,\"Second, with comma\",APPROVER2,,2026-12-15,\r\n";
        var token = await TestApp.Token(c, url);
        var form = Multipart(token, ("eventId", ev.ToString()));
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv)); file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "points.csv");
        var preview = await (await c.PostAsync($"/Compliance/Import?eventId={ev}&handler=Preview", form)).Content.ReadAsStringAsync();
        Assert.Contains("Import 2 action point(s)", preview);
        var confirm = Multipart(token, ("eventId", ev.ToString()), ("csv", csv));               // browsers may send CRLF; hash is normalised
        var r = await c.PostAsync($"/Compliance/Import?eventId={ev}&handler=Confirm", confirm);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var pts = R.EventPoints(ev, P("cs"));
        Assert.Equal(2, pts.Count);
        Assert.Equal("Second, with comma", pts[1].PointText);
        Assert.Single(R.SupportOfficers(pts[0].Id));
        // the same file again is refused
        var again = await c.PostAsync($"/Compliance/Import?eventId={ev}&handler=Confirm", Multipart(token, ("eventId", ev.ToString()), ("csv", csv.Replace("\r\n", "\n"))));
        Assert.Equal(2, R.EventPoints(ev, P("cs")).Count);
        // a file with one bad row imports nothing
        var bad = "ActionPoint,PrimaryOfficer,DueDate\nGood,staff,31.12.2026\nBad,nobody,31.12.2026\n";
        Assert.Equal(HttpStatusCode.Redirect, (await c.PostAsync($"/Compliance/Import?eventId={ev}&handler=Confirm", Multipart(token, ("eventId", ev.ToString()), ("csv", bad)))).StatusCode);
        Assert.Equal(2, R.EventPoints(ev, P("cs")).Count);
        _ = again;
    }

    [Fact]
    public void Import_rows_are_checked()
    {
        var rows = _app.Get<ComplianceImport>().Parse("ActionPoint,PrimaryOfficer,DueDate,Priority,Category\nX,staff,31/12/2026,Urgent,Nonsense\n,cs,bad,,\n", _app.User("cs").Id, out var header);
        Assert.Null(header);
        Assert.Contains(rows[0].Errors, e => e.Contains("Priority"));
        Assert.Contains(rows[0].Errors, e => e.Contains("Category"));
        Assert.Contains(rows[1].Errors, e => e.Contains("Action point is required"));
        Assert.Contains(rows[1].Errors, e => e.Contains("yourself"));
        _app.Get<ComplianceImport>().Parse("Foo,Bar\n1,2\n", 0, out var h2);
        Assert.Contains("Unknown column", h2);
        Assert.Null(ComplianceImport.Decode(new byte[] { 0x41, 0x00, 0x42 }, out var err));
        Assert.NotNull(err);
        Assert.Null(ComplianceImport.Decode(new byte[] { 0x41, 0xE9, 0x42 }, out _));             // Windows-1252 é: not UTF-8
    }

    [Fact]
    public async Task No_file_can_be_uploaded_except_the_csv_import_and_bodies_are_bounded()
    {
        var id = Point(Event());
        var c = await _app.SignIn("staff");
        var token = await TestApp.Token(c, $"/Compliance/Point/{id}");
        var withFile = Multipart(token, ("asOn", D(0)), ("text", "x"), ("proposed", "Ongoing"), ("rv", Rv(id).ToString()));
        withFile.Add(new ByteArrayContent(new byte[] { 0x25, 0x50, 0x44, 0x46 }), "scan", "scan.pdf");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsync($"/Compliance/Point/{id}?handler=Update", withFile)).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsync($"/compliance/point/{id}?handler=Update", new StringContent("x", Encoding.UTF8, "text/plain"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsync("/Compliance/ImportX", Multipart(token))).StatusCode);
        var huge = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["text"] = new string('a', 300_000) });
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await c.PostAsync($"/Compliance/Point/{id}?handler=Update", huge)).StatusCode);
        Assert.Null(R.Point(id)!.PendingUpdateId);
        // a pasted encoded file is refused as text
        var b64 = Convert.ToBase64String(new byte[600]);
        Assert.Throws<WorkflowException>(() => W.SubmitUpdate(id, D(0), "See " + b64, CStatus.Ongoing, null, Rv(id), P("staff")));
        // the form buffer keeps an import-size body in memory (never spilled to a temporary file)
        Assert.True(_app.Get<IOptions<FormOptions>>().Value.MemoryBufferThreshold > UploadGuard.ImportBodyLimit);
    }

    // ------------------------------------------------------------------ reminders, transfer, desk, backup

    [Fact]
    public void Daily_reminders_are_aggregated_and_not_repeated()
    {
        var ev = Event();
        var a = Point(ev, "approver1", dueInDays: -2);
        var b = Point(ev, "approver1", dueInDays: 5);
        Point(ev, "approver1", dueInDays: 1);
        var today = _app.Get<Fmt>().Today;
        long Reminders() => Db.Scalar<long>("SELECT COUNT(*) FROM Notifications WHERE Type = 'Compliance.Reminder' AND UserId = @u", new { u = _app.User("approver1").Id });
        W.DailyReminders(today);
        Assert.Equal(1, Reminders());                                                            // one notification for three points
        var text = Db.Scalar<string>("SELECT Title FROM Notifications WHERE Type = 'Compliance.Reminder' AND UserId = @u", new { u = _app.User("approver1").Id })!;
        Assert.Contains("overdue", text);
        Assert.Contains("due within", text);
        W.DailyReminders(today);
        Assert.Equal(1, Reminders());                                                            // nothing new the same day
        W.DailyReminders(today.AddDays(7));                                                      // overdue repeats weekly
        Assert.Equal(2, Reminders());
        Assert.True(Db.Scalar<long>("SELECT COUNT(*) FROM Notifications WHERE Type = 'Compliance.Digest'") > 0);
        _ = a; _ = b;
    }

    [Fact]
    public void Bulk_transfer_moves_open_points_only()
    {
        var ev = Event();
        var a = Point(ev, "approver1");
        var b = Point(ev, "approver1");
        W.Close(b, "done", "IA report", Rv(b), P("cs"));
        var c = W.CreatePoint(ev, new CompliancePointInput { PointText = "Shared", PrimaryOfficerUserId = _app.User("staff").Id, SupportOfficerIds = { _app.User("approver1").Id }, DueDate = D(9) }, P("cs"));
        Assert.Throws<WorkflowException>(() => W.Transfer(_app.User("approver1").Id, _app.User("cs").Id, "both", P("cs")));     // not to yourself
        var moved = W.Transfer(_app.User("approver1").Id, _app.User("approver2").Id, "both", P("cs"));
        Assert.True(moved >= 2);
        Assert.Equal(_app.User("approver2").Id, R.Point(a)!.PrimaryOfficerUserId);
        Assert.Equal(_app.User("approver1").Id, R.Point(b)!.PrimaryOfficerUserId);               // closed: unchanged
        Assert.Equal(new[] { _app.User("approver2").Id }, R.SupportOfficers(c).Select(x => x.Id).ToArray());
    }

    [Fact]
    public void My_Desk_counts_officer_items_and_the_Cell_queue()
    {
        var id = Point(Event(), "approver1", dueInDays: -1);
        var desk = _app.Get<DeskService>();
        Assert.True(desk.Counts(P("approver1")).ComplianceMine >= 1);
        Assert.True(desk.Counts(P("approver1")).ComplianceAssigned);
        var before = desk.Counts(P("cs")).ComplianceCell;
        W.SubmitUpdate(id, D(0), "Done", CStatus.Ongoing, null, Rv(id), P("approver1"));
        Thread.Sleep(1100);                                                                      // counts read within 1 s of a change are re-read
        Assert.Equal(before + 1, desk.Counts(P("cs")).ComplianceCell);
        Assert.False(desk.Counts(P("bdu")).ComplianceAssigned);
    }

    [Fact]
    public void Backups_include_the_compliance_tables()
    {
        Point(Event());
        var backup = _app.Get<BackupService>().Create(_app.Get<AuditService>(), "test");
        var dir = Path.Combine(_app.Root, "restored");
        Directory.CreateDirectory(dir);
        using (var zip = ZipFile.OpenRead(backup.FullPath)) zip.GetEntry("data/intranet.db")!.ExtractToFile(Path.Combine(dir, "x.db"));
        using var c = new SqliteConnection($"Data Source={Path.Combine(dir, "x.db")};Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM CompliancePoints";
        Assert.True((long)cmd.ExecuteScalar()! > 0);
    }

    [Fact]
    public async Task Pages_render_for_each_role()
    {
        var ev = Event();
        var id = Point(ev, "staff");
        var cs = await _app.SignIn("cs");
        foreach (var url in new[] { "/Compliance", "/Compliance/Points", "/Compliance/Events", $"/Compliance/Event/{ev}", $"/Compliance/Point/{id}", "/Compliance/Review",
                     $"/Compliance/PointEdit?eventId={ev}", "/Compliance/EventEdit", $"/Compliance/Import?eventId={ev}", "/Compliance/Atr", $"/Compliance/Atr?events={ev}&preview=true",
                     "/Compliance/Snapshots", "/Compliance/Transfer", "/Compliance/Members", "/Compliance/Mine", "/Desk" })
            Assert.True((await cs.GetAsync(url)).StatusCode == HttpStatusCode.OK, url);
        var staff = await _app.SignIn("staff");
        Assert.Contains("Compliance", await staff.GetStringAsync("/"));                         // menu tab for an officer
        Assert.Equal(HttpStatusCode.Redirect, (await staff.GetAsync("/Compliance")).StatusCode); // officers go to My action points
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/Compliance/Mine")).StatusCode);
        var docx = await cs.GetAsync($"/Compliance/Atr?handler=Download&format=docx&template=ObservationReply&events={ev}&asOn={D(0)}");
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", docx.Content.Headers.ContentType!.MediaType);
        var export = await cs.GetAsync("/Compliance/Points?handler=Export&status=all");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
    }
}
