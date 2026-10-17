using System.Diagnostics;
using System.Net;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using Xunit.Abstractions;

namespace NabconsIntranet.Tests;

/// <summary>
/// Performance budgets at the brief's volume (500 users, 300,000 notifications; e-Note volumes are added by the e-Note seed).
/// Slow to set up, so it only runs when NABCONS_BENCH=1:   NABCONS_BENCH=1 dotnet test --filter PerformanceTests
/// Results are printed and recorded in docs/TEST-SCRIPT.md.
/// </summary>
public class PerformanceTests(ITestOutputHelper output) : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private static bool Enabled => Environment.GetEnvironmentVariable("NABCONS_BENCH") == "1";

    public static void SeedUsers(SqliteConnection c, int total)
    {
        using var tx = c.BeginTransaction();
        var existing = Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM Users");
        var hash = PasswordHasher.Hash(TestApp.Password);
        for (var i = (int)existing; i < total; i++)
            Db.Exec(c, tx, """
                INSERT INTO Users (Username, DisplayName, Vertical, Roles, PasswordHash, MustChangePassword, IsActive, SecurityStamp, CreatedAt)
                VALUES (@u, @n, @v, 'Staff', @hash, 0, 1, 'x', @now)
                """, new { u = "bench" + i, n = "Bench User " + i, v = (i % 5) switch { 0 => "BDU", 1 => "HR & Administration", 2 => "Finance & Accounts", 3 => "IT & Digital", _ => "Rural Infrastructure" }, hash, now = Db.Now() });
        tx.Commit();
    }

    public static void SeedNotifications(SqliteConnection c, int total, long[] userIds)
    {
        var rnd = new Random(7);
        using var tx = c.BeginTransaction();
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Notifications (UserId, Type, Title, Link, ActorName, CreatedAt, ReadAt) VALUES ($u, 'Request.Assigned', $t, '/Requests', 'Bench', $at, $read)";
        var pu = cmd.Parameters.Add("$u", SqliteType.Integer); var pt = cmd.Parameters.Add("$t", SqliteType.Text);
        var pa = cmd.Parameters.Add("$at", SqliteType.Text); var pr = cmd.Parameters.Add("$read", SqliteType.Text);
        var now = DateTime.UtcNow;
        for (var i = 0; i < total; i++)
        {
            var at = now.AddMinutes(-rnd.Next(0, 60 * 24 * 300));
            pu.Value = userIds[rnd.Next(userIds.Length)]; pt.Value = "Bench notification " + i; pa.Value = at.ToString("o");
            pr.Value = rnd.Next(10) < 8 ? at.AddHours(2).ToString("o") : DBNull.Value;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public static (double p50, double p95, double max) Measure(int runs, Action a)
    {
        a(); // warm-up
        var t = new List<double>();
        for (var i = 0; i < runs; i++) { var sw = Stopwatch.StartNew(); a(); t.Add(sw.Elapsed.TotalMilliseconds); }
        t.Sort();
        return (t[t.Count / 2], t[(int)(t.Count * 0.95)], t[^1]);
    }

    [Fact]
    public async Task Notification_and_desk_budgets_at_volume()
    {
        if (!Enabled) { output.WriteLine("Skipped: set NABCONS_BENCH=1 to run the performance benchmark."); return; }
        var db = _app.Get<Db>();
        var sw = Stopwatch.StartNew();
        using (var c = db.Open())
        {
            SeedUsers(c, 500);
            var ids = Db.Query<IdOnly>(c, null, "SELECT Id FROM Users").Select(x => x.Id).ToArray();
            SeedNotifications(c, 300_000, ids);
        }
        output.WriteLine($"Seeded 500 users / 300,000 notifications in {sw.Elapsed.TotalSeconds:0.0} s");

        var a1 = await _app.SignIn("approver1");
        var desk = _app.Get<DeskService>();
        var tracker = _app.Get<ChangeTracker>();
        var principal = _app.Principal("approver1");
        var uid = principal.UserId();

        // 1. poll, nothing changed (the normal case): answered from memory, 304
        var first = await a1.GetAsync("/api/notifications/summary");
        var etag = first.Headers.ETag!.Tag;
        var poll304 = new List<double>();
        for (var i = 0; i < 200; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/summary");
            req.Headers.TryAddWithoutValidation("If-None-Match", etag);
            var t = Stopwatch.StartNew();
            var r = await a1.SendAsync(req);
            poll304.Add(t.Elapsed.TotalMilliseconds);
            Assert.Equal(HttpStatusCode.NotModified, r.StatusCode);
        }
        poll304.Sort();
        // 2. poll after a change (cache miss: counts re-read from the database)
        var miss = Measure(100, () => { tracker.TouchUser(uid); Thread.Sleep(0); _ = desk.Counts(principal); });
        // 3. My Desk page and the Notifications page (full HTTP round trip, in memory)
        var deskPage = new List<double>(); var notesPage = new List<double>();
        for (var i = 0; i < 50; i++)
        {
            var t = Stopwatch.StartNew(); (await a1.GetAsync("/Desk")).EnsureSuccessStatusCode(); deskPage.Add(t.Elapsed.TotalMilliseconds);
            t = Stopwatch.StartNew(); (await a1.GetAsync("/Notifications?page=3")).EnsureSuccessStatusCode(); notesPage.Add(t.Elapsed.TotalMilliseconds);
        }
        deskPage.Sort(); notesPage.Sort();
        double P95(List<double> l) => l[(int)(l.Count * 0.95)];
        output.WriteLine($"Poll (304, unchanged):        p50 {poll304[poll304.Count / 2]:0.00} ms  p95 {P95(poll304):0.00} ms   budget < 20 ms");
        output.WriteLine($"Counts after a change (DB):   p50 {miss.p50:0.00} ms  p95 {miss.p95:0.00} ms   budget < 20 ms");
        output.WriteLine($"My Desk page:                 p50 {deskPage[deskPage.Count / 2]:0.0} ms  p95 {P95(deskPage):0.0} ms   budget < 250 ms");
        output.WriteLine($"Notifications page 3:         p50 {notesPage[notesPage.Count / 2]:0.0} ms  p95 {P95(notesPage):0.0} ms");
        Assert.True(P95(poll304) < 20, "poll budget");
        Assert.True(miss.p95 < 20, "count budget");
        Assert.True(P95(deskPage) < 250, "My Desk budget");

        // housekeeping over the volume stays quick (batched deletes)
        var hk = Stopwatch.StartNew();
        var res = _app.Get<HousekeepingService>().Run();
        output.WriteLine($"Housekeeping: removed {res.ReadDeleted:N0} read + {res.UnreadDeleted:N0} unread in {hk.Elapsed.TotalSeconds:0.0} s");
    }

    private sealed class IdOnly { public long Id { get; set; } }
}
