using System.Diagnostics;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using Xunit.Abstractions;

namespace NabconsIntranet.Tests;

/// <summary>
/// Compliance performance budgets at the DD-07 §13 design volume (5 years): 6,000 action points, 40,000 updates,
/// 30,000 history rows, 2,000 ATR snapshots × 40 rows, 500 users. Runs only with NABCONS_BENCH=1:
///   NABCONS_BENCH=1 dotnet test --filter ComplianceBenchTests       (results are recorded in docs/TEST-SCRIPT.md)
/// </summary>
public class ComplianceBenchTests(ITestOutputHelper output) : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private static bool Enabled => Environment.GetEnvironmentVariable("NABCONS_BENCH") == "1";

    private static (double p50, double p95, double max) Stats(List<double> t) { t.Sort(); return (t[t.Count / 2], t[(int)(t.Count * 0.95)], t[^1]); }

    private async Task<(double p50, double p95, double max)> Time(int runs, Func<Task> a)
    {
        await a();
        var t = new List<double>();
        for (var i = 0; i < runs; i++) { var sw = Stopwatch.StartNew(); await a(); t.Add(sw.Elapsed.TotalMilliseconds); }
        return Stats(t);
    }

    private void Row(string what, (double p50, double p95, double max) s, string budget) =>
        output.WriteLine($"{what,-46} p50 {s.p50,7:0.0} ms   p95 {s.p95,7:0.0} ms   max {s.max,7:0.0} ms   budget {budget}");

    private static readonly string[] Sources = { "Board", "AuditCommittee", "BoardCommittee", "NabardInspection", "StatutoryAudit", "InternalAudit", "ReviewMeeting", "Other" };
    private static readonly string[] Statuses = { "Ongoing", "Ongoing", "PendingExternal", "ProposedClosure", "Completed", "Completed", "Completed", "Dropped" };
    private static readonly string[] Words = "reconcile ledger provision review policy committee audit observation compliance vendor tender asset register tax deduction report board".Split(' ');

    private static void Seed(SqliteConnection c, List<long> users, long forumId, long boardForumId)
    {
        var rnd = new Random(7);
        string Text(int n) => string.Join(' ', Enumerable.Range(0, n).Select(_ => Words[rnd.Next(Words.Length)]));
        string Day(int offset) => DateTime.UtcNow.AddDays(offset).ToString("yyyy-MM-dd");
        var now = DateTime.UtcNow.ToString("o");
        using var tx = c.BeginTransaction();
        SqliteCommand Cmd(string sql) { var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; return cmd; }
        var ev = Cmd("INSERT INTO ComplianceEvents (SourceType, ForumId, Forum, EventNo, Title, HeldOn, ReportingCycle, CreatedByUserId, CreatedByName, CreatedAt, UpdatedAt) VALUES ($s, $f, 'Bench forum', $n, $t, $h, 'Quarterly', 1, 'bench', $now, $now) RETURNING Id");
        var ps = ev.Parameters.Add("$s", SqliteType.Text); var pf = ev.Parameters.Add("$f", SqliteType.Integer); var pn = ev.Parameters.Add("$n", SqliteType.Text);
        var pt = ev.Parameters.Add("$t", SqliteType.Text); var ph = ev.Parameters.Add("$h", SqliteType.Text); ev.Parameters.AddWithValue("$now", now);
        var events = new List<long>();
        for (var i = 0; i < 300; i++)
        {
            ps.Value = Sources[i % Sources.Length]; pf.Value = forumId; pn.Value = $"{i + 1}th"; pt.Value = "Bench event " + i; ph.Value = Day(-5 * i);
            events.Add((long)ev.ExecuteScalar()!);
        }
        var pt2 = Cmd("""
            INSERT INTO CompliancePoints (EventId, Number, FinancialYear, Seq, RefNo, PointText, Priority, OwnerVertical, PrimaryOfficerUserId, RaisedOn, OriginalDueDate, CurrentDueDate,
                Status, AtrText, AtrAsOn, ClosedAt, CreatedByName, CreatedAt, UpdatedAt)
            VALUES ($e, $num, '2026-27', $seq, $ref, $text, 'Medium', 'Finance & Accounts', $o, $raised, $due, $due, $st, $atr, $asOn, $closed, 'bench', $now, $now) RETURNING Id
            """);
        var pe = pt2.Parameters.Add("$e", SqliteType.Integer); var pnum = pt2.Parameters.Add("$num", SqliteType.Text); var pseq = pt2.Parameters.Add("$seq", SqliteType.Integer);
        var pref = pt2.Parameters.Add("$ref", SqliteType.Text); var ptext = pt2.Parameters.Add("$text", SqliteType.Text); var po = pt2.Parameters.Add("$o", SqliteType.Integer);
        var praised = pt2.Parameters.Add("$raised", SqliteType.Text); var pdue = pt2.Parameters.Add("$due", SqliteType.Text); var pst = pt2.Parameters.Add("$st", SqliteType.Text);
        var patr = pt2.Parameters.Add("$atr", SqliteType.Text); var pas = pt2.Parameters.Add("$asOn", SqliteType.Text); var pcl = pt2.Parameters.Add("$closed", SqliteType.Text);
        pt2.Parameters.AddWithValue("$now", now);
        var points = new List<long>();
        for (var i = 0; i < 6000; i++)
        {
            pe.Value = events[i % events.Count]; pnum.Value = $"BENCH/{i:D5}"; pseq.Value = i; pref.Value = "Para " + (i % 40 + 1); ptext.Value = Text(40);
            po.Value = users[rnd.Next(users.Count)]; praised.Value = Day(-rnd.Next(0, 1500)); pdue.Value = Day(rnd.Next(-200, 200));
            var st = Statuses[rnd.Next(Statuses.Length)]; pst.Value = st; patr.Value = Text(30); pas.Value = Day(-rnd.Next(1, 90));
            pcl.Value = st is "Completed" or "Dropped" ? DateTime.UtcNow.AddDays(-rnd.Next(1, 365)).ToString("o") : DBNull.Value;
            points.Add((long)pt2.ExecuteScalar()!);
        }
        var up = Cmd("INSERT INTO ComplianceUpdates (PointId, SubmittedByUserId, SubmittedByName, SubmittedAt, AsOn, UpdateText, ProposedStatus, ReviewState, AtrText) VALUES ($p, $u, 'Bench', $now, $asOn, $t, 'Ongoing', 'Accepted', $t)");
        var upp = up.Parameters.Add("$p", SqliteType.Integer); var upu = up.Parameters.Add("$u", SqliteType.Integer); var upa = up.Parameters.Add("$asOn", SqliteType.Text); var upt = up.Parameters.Add("$t", SqliteType.Text);
        up.Parameters.AddWithValue("$now", now);
        for (var i = 0; i < 40000; i++) { upp.Value = points[rnd.Next(points.Count)]; upu.Value = users[rnd.Next(users.Count)]; upa.Value = Day(-rnd.Next(1, 700)); upt.Value = Text(35); up.ExecuteNonQuery(); }
        var hi = Cmd("INSERT INTO ComplianceHistory (PointId, EventId, At, ActorName, Action, Detail) VALUES ($p, NULL, $at, 'Bench', 'UpdateAccepted', $d)");
        var hp = hi.Parameters.Add("$p", SqliteType.Integer); var ha = hi.Parameters.Add("$at", SqliteType.Text); var hd = hi.Parameters.Add("$d", SqliteType.Text);
        for (var i = 0; i < 30000; i++) { hp.Value = points[rnd.Next(points.Count)]; ha.Value = DateTime.UtcNow.AddDays(-rnd.Next(2, 900)).ToString("o"); hd.Value = Text(8); hi.ExecuteNonQuery(); }
        var sn = Cmd("INSERT INTO ComplianceSnapshots (Template, ForumId, Forum, Title, SubmittedTo, SubmittedOn, AsOn, OptionsJson, HeaderJson, ItemCount, Sha256, CreatedByUserId, CreatedByName, CreatedAt) VALUES ('BoardAtr', $f, 'Bench forum', 'Bench ATR', 'Board', $d, $d, '{}', '{}', 40, 'X', 1, 'bench', $now) RETURNING Id");
        var snf = sn.Parameters.Add("$f", SqliteType.Integer); var snd = sn.Parameters.Add("$d", SqliteType.Text); sn.Parameters.AddWithValue("$now", now);
        var si = Cmd("INSERT INTO ComplianceSnapshotItems (SnapshotId, Seq, Section, PointId, Cells, ReportedText, Status) VALUES ($s, $q, 0, $p, $c, $r, 'Ongoing')");
        var sis = si.Parameters.Add("$s", SqliteType.Integer); var siq = si.Parameters.Add("$q", SqliteType.Integer); var sip = si.Parameters.Add("$p", SqliteType.Integer);
        var sic = si.Parameters.Add("$c", SqliteType.Text); var sir = si.Parameters.Add("$r", SqliteType.Text);
        for (var s = 0; s < 2000; s++)
        {
            snf.Value = s % 2 == 0 ? forumId : boardForumId; snd.Value = Day(-400 - s);
            var id = (long)sn.ExecuteScalar()!;
            for (var k = 0; k < 40; k++)
            {
                sis.Value = id; siq.Value = k + 1; sip.Value = points[rnd.Next(points.Count)]; var t = Text(30); sic.Value = $"[\"Para\",\"{t}\",\"{t}\",\"Ongoing\"]"; sir.Value = t; si.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    [Fact]
    public async Task Compliance_budgets_at_the_design_volume()
    {
        if (!Enabled) { output.WriteLine("Skipped: set NABCONS_BENCH=1 to run the compliance benchmark."); return; }
        var db = _app.Get<Db>();
        var masters = _app.Get<MasterRepository>();
        var forum = masters.List(MasterTypes.ComplianceForum).First(m => m.Name == "Statutory Auditor").Id;
        var boardForum = masters.List(MasterTypes.ComplianceForum).First(m => m.Name == "Board of Directors").Id;
        var sw = Stopwatch.StartNew();
        List<long> users;
        using (var c = db.Open())
        {
            users = BenchSeed.Users(c, 500, PasswordHasher.Hash(BenchSeed.Password));
            Seed(c, users, forum, boardForum);
        }
        output.WriteLine($"Seeded 300 events, 6,000 points, 40,000 updates, 30,000 history rows, 2,000 snapshots × 40 rows in {sw.Elapsed.TotalSeconds:0} s " +
                         $"(database {new FileInfo(_app.Get<IntranetOptions>().DatabasePath).Length / 1024 / 1024} MB)");
        db.Exec("PRAGMA optimize;");

        // three Board meetings × 40 points for the ATR budget, through the real workflow
        var w = _app.Get<ComplianceWorkflow>();
        var cs = _app.Principal("cs");
        var meetings = new List<long>();
        for (var m = 0; m < 3; m++)
        {
            var ev = w.CreateEvent(new ComplianceEventInput { SourceType = ComplianceSource.Board, ForumId = boardForum, EventNo = $"{100 + m}th", Title = "Board meeting", HeldOn = DateTime.UtcNow.AddDays(-30 - 60 * m).ToString("yyyy-MM-dd") }, cs);
            for (var k = 0; k < 40; k++)
                w.CreatePoint(ev, new CompliancePointInput { RefNo = $"Memo No. {k + 1}", RefSubject = "Subject of the memorandum " + k, PointText = string.Join(' ', Enumerable.Repeat("place the revised policy before the Board after vetting", 4)),
                    PrimaryOfficerUserId = users[k], DueDate = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"), InitialAtrText = string.Join(' ', Enumerable.Repeat("action taken and reported to the committee", 4)), InitialAsOn = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd") }, cs);
            meetings.Add(ev);
        }
        var c1 = await _app.SignIn("cs");
        var dash = await Time(40, async () => (await c1.GetAsync("/Compliance")).EnsureSuccessStatusCode());
        Row("Dashboard (6,000 points)", dash, "< 300 ms");
        var list = await Time(40, async () => (await c1.GetAsync("/Compliance/Points?status=overdue")).EnsureSuccessStatusCode());
        Row("Points list, overdue filter, page 1", list, "< 300 ms");
        var search = await Time(40, async () => (await c1.GetAsync("/Compliance/Points?status=all&q=reconcile+ledger")).EnsureSuccessStatusCode());
        Row("Points list, text search (all statuses)", search, "< 400 ms");
        Row("Events list", await Time(30, async () => (await c1.GetAsync("/Compliance/Events")).EnsureSuccessStatusCode()), "< 300 ms");
        var bench0 = await _app.SignIn("bench0", BenchSeed.Password);
        Row("Officer: My action points", await Time(30, async () => (await bench0.GetAsync("/Compliance/Mine")).EnsureSuccessStatusCode()), "< 300 ms");
        Row("Officer: My Desk", await Time(30, async () => (await bench0.GetAsync("/Desk")).EnsureSuccessStatusCode()), "< 250 ms");
        var q = "&template=BoardAtr&" + string.Join("&", meetings.Select(m => "events=" + m)) + "&asOn=" + _app.Get<Fmt>().Today.ToString("yyyy-MM-dd");
        var docx = await Time(5, async () => (await c1.GetAsync("/Compliance/Atr?handler=Download&format=docx" + q)).EnsureSuccessStatusCode());
        Row("ATR 3 meetings × 40 points, DOCX", docx, "< 5,000 ms");
        var pdf = await Time(5, async () => (await c1.GetAsync("/Compliance/Atr?handler=Download&format=pdf" + q)).EnsureSuccessStatusCode());
        Row("ATR 3 meetings × 40 points, PDF", pdf, "< 5,000 ms");
        var xs = Stopwatch.StartNew();
        var x = await c1.GetAsync("/Compliance/Points?handler=Export&status=all");
        output.WriteLine($"Points Excel export (all 6,120 rows): {xs.ElapsedMilliseconds} ms, {(await x.Content.ReadAsByteArrayAsync()).Length / 1024} KB");
        var rem = Stopwatch.StartNew();
        w.DailyReminders(_app.Get<Fmt>().Today);
        output.WriteLine($"Daily reminders over 6,000 points: {rem.ElapsedMilliseconds} ms");

        Assert.True(dash.p95 < 300, "dashboard budget");
        Assert.True(list.p95 < 300, "list budget");
        Assert.True(search.p95 < 400, "search budget");
        Assert.True(docx.p95 < 5000 && pdf.p95 < 5000, "ATR budget");
    }
}
