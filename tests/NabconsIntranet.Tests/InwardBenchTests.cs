using System.Diagnostics;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;
using Xunit.Abstractions;

namespace NabconsIntranet.Tests;

/// <summary>
/// Inward performance budgets at the DD-08 §13 design volume: 250,000 items (10% invoices) over 12 offices, with an
/// operator of 3 offices. Runs only with NABCONS_BENCH=1:
///   NABCONS_BENCH=1 dotnet test --filter InwardBenchTests       (results are recorded in docs/TEST-SCRIPT.md)
/// </summary>
public class InwardBenchTests(ITestOutputHelper output) : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private readonly List<string> _over = new();
    private static bool Enabled => Environment.GetEnvironmentVariable("NABCONS_BENCH") == "1";

    private static (double p50, double p95, double max) Time(int runs, Action a)
    {
        a();
        var t = new List<double>();
        for (var i = 0; i < runs; i++) { var sw = Stopwatch.StartNew(); a(); t.Add(sw.Elapsed.TotalMilliseconds); }
        t.Sort();
        return (t[t.Count / 2], t[(int)(t.Count * 0.95)], t[^1]);
    }

    private void Row(string what, (double p50, double p95, double max) s, double budget)
    {
        output.WriteLine($"{what,-44} p50 {s.p50,7:0.0} ms   p95 {s.p95,7:0.0} ms   max {s.max,7:0.0} ms   budget {budget} ms");
        if (s.p95 >= budget) _over.Add($"{what}: p95 {s.p95:0} ms over budget {budget} ms");
    }

    private static readonly string[] Words = "proposal evaluation watershed survey invoice tender report request meeting payment project fpo baseline training".Split(' ');

    [Fact]
    public void Budgets_at_250k_items()
    {
        if (!Enabled) return;
        var db = _app.Get<Db>();
        var offices = _app.Get<InwardRepository>().Offices().Take(12).Select(o => o.Id).ToList();
        var users = db.Query<PersonOption>("SELECT Id, DisplayName, Username FROM Users").Select(u => u.Id).ToList();
        var rnd = new Random(11);
        var sw = Stopwatch.StartNew();
        using (var c = db.Open())
        using (var tx = c.BeginTransaction())
        {
            SqliteCommand Cmd(string sql) { var cmd = c.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; return cmd; }
            var item = Cmd("""
                INSERT INTO InwardItems (Number, OfficeId, OfficeName, OfficeCode, FinancialYear, Serial, ItemType, Mode, ReceivedAt, SenderName, Subject, Priority,
                    ActionRequired, ReplyDueOn, KeptAt, Status, HolderUserId, InwardedByUserId, InwardedByName, InwardedAt, ClosedAt, UpdatedAt)
                VALUES ($n, $o, 'Office', 'B', '2025-26', $s, $t, 'Post', $r, 'Sender', $subj, 'Normal', 'Reply', $due, 'Rack', $st, $h, $by, 'bench', $r, $closed, $r) RETURNING Id
                """);
            var p = new Dictionary<string, SqliteParameter>();
            foreach (var k in new[] { "$n", "$o", "$s", "$t", "$r", "$subj", "$due", "$st", "$h", "$by", "$closed" }) p[k] = item.Parameters.Add(k, SqliteType.Text);
            var part = Cmd("INSERT OR IGNORE INTO InwardParticipants (ItemId, UserId) VALUES ($i, $u)");
            var pi = part.Parameters.Add("$i", SqliteType.Integer); var pu = part.Parameters.Add("$u", SqliteType.Integer);
            var inv = Cmd("""
                INSERT INTO InvoiceDetails (ItemId, VendorName, InvoiceNo, InvoiceDate, InvoiceFy, TotalPaise, SettledPaise, PaymentDueOn, InvoiceStatus, DupKey, IsMsme)
                VALUES ($i, $v, $no, $d, '2025-26', 100000, $paid, $d, $st, $k, $m)
                """);
            var ii = inv.Parameters.Add("$i", SqliteType.Integer); var iv = inv.Parameters.Add("$v", SqliteType.Text); var ino = inv.Parameters.Add("$no", SqliteType.Text);
            var id_ = inv.Parameters.Add("$d", SqliteType.Text); var ipaid = inv.Parameters.Add("$paid", SqliteType.Integer); var ist = inv.Parameters.Add("$st", SqliteType.Text);
            var ik = inv.Parameters.Add("$k", SqliteType.Text); var im = inv.Parameters.Add("$m", SqliteType.Integer);
            var fts = Cmd("INSERT INTO InwardSearch (rowid, Number, Sender, Subject) VALUES ($i, $n, 'Sender', $subj)");
            var fi = fts.Parameters.Add("$i", SqliteType.Integer); var fn = fts.Parameters.Add("$n", SqliteType.Text); var fs = fts.Parameters.Add("$subj", SqliteType.Text);
            for (var n = 0; n < 250_000; n++)
            {
                var invoice = n % 10 == 0;
                var open = rnd.Next(100) < 12;
                var at = DateTime.UtcNow.AddMinutes(-n * 7);
                // ~500 people as in the design volume: the demo users hold 2% of the items, synthetic ids the rest
                var holder = rnd.Next(100) < 2 ? users[rnd.Next(users.Count)] : 100_000 + rnd.Next(500);
                var by = n % 50 == 0 ? users[n % users.Count] : 100_000 + (n % 500);
                var subj = string.Join(' ', Enumerable.Range(0, 6).Select(_ => Words[rnd.Next(Words.Length)]));
                p["$n"].Value = $"B/{n:D6}"; p["$o"].Value = offices[n % offices.Count]; p["$s"].Value = n; p["$t"].Value = invoice ? "Invoice" : "Letter";
                p["$r"].Value = at.ToString("o"); p["$subj"].Value = subj; p["$due"].Value = invoice ? DBNull.Value : at.AddDays(7).ToString("yyyy-MM-dd");
                p["$st"].Value = open ? (invoice && n % 20 == 0 ? "WithAccounts" : "WithHolder") : "Closed";
                p["$h"].Value = holder; p["$by"].Value = by; p["$closed"].Value = open ? DBNull.Value : at.AddDays(3).ToString("o");
                var id = (long)item.ExecuteScalar()!;
                pi.Value = id; pu.Value = holder; part.ExecuteNonQuery(); pu.Value = by; part.ExecuteNonQuery();
                fi.Value = id; fn.Value = $"B/{n:D6}"; fs.Value = subj; fts.ExecuteNonQuery();
                if (invoice)
                {
                    ii.Value = id; iv.Value = "Vendor " + (n % 500); ino.Value = "INV" + n; id_.Value = at.ToString("yyyy-MM-dd"); ipaid.Value = open ? 0 : 100000;
                    ist.Value = open ? (n % 20 == 0 ? "SentToAccounts" : "WithVerifier") : "Paid"; ik.Value = "N:v|INV" + n + "|2025-26"; im.Value = n % 3 == 0 ? 1 : 0;
                    inv.ExecuteNonQuery();
                }
            }
            tx.Commit();
        }
        db.Exec("ANALYZE");
        if (Environment.GetEnvironmentVariable("NABCONS_BENCH_KEEP") is { Length: > 0 } keep) db.Exec("VACUUM INTO @keep", new { keep });
        output.WriteLine($"Seeded 250,000 items in {sw.Elapsed.TotalSeconds:0} s");

        var w = _app.Get<InwardWorkflow>();
        var admin = _app.Principal("admin");
        foreach (var o in offices.Take(3)) w.Grant(_app.User("bdu").Id, InwardRole.Operator, o, admin);
        var op = _app.Principal("bdu");
        var viewer = _app.Principal("coo");
        var acc = _app.Principal("approver2");
        var repo = _app.Get<InwardRepository>();
        Row("dashboard (operator, 3 offices)", Time(20, () => repo.Dashboard(op, null)), 300);
        Row("dashboard (Inward Viewer, all)", Time(20, () => repo.Dashboard(viewer, null)), 300);
        Row("register page 1 (operator)", Time(10, () => repo.Register(op, new InwardFilter())), 300);
        Row("register page 1 (participant only)", Time(10, () => repo.Register(_app.Principal("staff"), new InwardFilter { Status = "all" })), 300);
        Row("invoice dashboard (Accounts)", Time(10, () => repo.InvoiceDashboard(acc)), 300);
        Row("search 'watershed survey' (viewer)", Time(10, () => repo.Register(viewer, new InwardFilter { Status = "all", Q = "watershed survey" })), 400);
        Row("My Desk counts", Time(20, () => { _app.Get<DeskService>().View(op); }), 300);
        var letter = w.Inward(new InwardInput
        {
            OfficeId = offices[0], ItemType = "Letter", Mode = "Post", ReceivedOn = _app.Get<Fmt>().Today.ToString("yyyy-MM-dd"), SenderName = "S", Subject = "Bench",
            ActionRequired = "Reply", KeptAt = "R",
        }, op, true);
        var users2 = new[] { _app.User("staff").Id, _app.User("approver1").Id };
        var turn = 0;
        Row("write: re-mark", Time(20, () => w.Mark(letter, users2[turn++ % 2], null, db.Scalar<long>("SELECT RowVersion FROM InwardItems WHERE Id = @letter", new { letter }), op)), 50);
        var today = _app.Get<Fmt>().Today.ToString("yyyy-MM-dd");
        var reports = _app.Get<InwardReports>();
        Row("register PDF, one day (operator)", Time(3, () =>
        {
            var rows = repo.RegisterRows(op, new InwardFilter { Status = "all", From = today, To = today }, InwardReports.MaxPdfRows, true)!;
            using var ms = new MemoryStream();
            ReportPdfWriter.Write(reports.Register(rows, "Inward register", "today", true), ms);
        }), 3000);
        Assert.True(_over.Count == 0, string.Join("; ", _over));
    }
}
