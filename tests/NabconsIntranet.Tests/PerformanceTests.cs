using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using Xunit.Abstractions;

namespace NabconsIntranet.Tests;

/// <summary>
/// Performance budgets at the brief's volume: 500 users, 20,000 notes, 200,000 movements, 300,000 notifications.
/// Slow to set up, so it only runs when NABCONS_BENCH=1:   NABCONS_BENCH=1 dotnet test --filter PerformanceTests
/// Results are recorded in docs/TEST-SCRIPT.md.
/// </summary>
public class PerformanceTests(ITestOutputHelper output) : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private static bool Enabled => Environment.GetEnvironmentVariable("NABCONS_BENCH") == "1";

    private static (double p50, double p95, double max) Stats(List<double> t)
    {
        t.Sort();
        return (t[t.Count / 2], t[(int)(t.Count * 0.95)], t[^1]);
    }

    private async Task<(double p50, double p95, double max)> Time(int runs, Func<Task> a)
    {
        await a();
        var t = new List<double>();
        for (var i = 0; i < runs; i++) { var sw = Stopwatch.StartNew(); await a(); t.Add(sw.Elapsed.TotalMilliseconds); }
        return Stats(t);
    }

    private void Row(string what, (double p50, double p95, double max) s, string budget) =>
        output.WriteLine($"{what,-44} p50 {s.p50,7:0.0} ms   p95 {s.p95,7:0.0} ms   max {s.max,7:0.0} ms   budget {budget}");

    [Fact]
    public async Task Budgets_at_the_brief_volume()
    {
        if (!Enabled) { output.WriteLine("Skipped: set NABCONS_BENCH=1 to run the performance benchmark."); return; }
        var db = _app.Get<Db>();
        var sw = Stopwatch.StartNew();
        List<long> users;
        using (var c = db.Open())
        {
            users = BenchSeed.Users(c, 500, PasswordHasher.Hash(BenchSeed.Password));
            BenchSeed.Notes(c, users);
            BenchSeed.Notifications(c, users);
        }
        output.WriteLine($"Seeded 500 users, 20,000 notes, 200,000 movements, 40,000 comments, 300,000 notifications in {sw.Elapsed.TotalSeconds:0} s " +
                         $"(database {new FileInfo(_app.Get<IntranetOptions>().DatabasePath).Length / 1024 / 1024} MB)");
        _app.Get<HousekeepingService>().Run();    // as every night: statistics for the query planner (and retention)

        // a bench user who holds notes, so every section has content
        var holder = db.Scalar<long>("SELECT CurrentHolderUserId FROM ENotes WHERE State = 'InMovement' GROUP BY CurrentHolderUserId ORDER BY COUNT(*) DESC LIMIT 1");
        var name = db.Scalar<string>("SELECT Username FROM Users WHERE Id = @holder", new { holder })!;
        var c1 = await _app.SignIn(name, BenchSeed.Password);
        var principal = _app.Principal(name);
        var desk = _app.Get<DeskService>();
        var tracker = _app.Get<ChangeTracker>();

        var first = await c1.GetAsync("/api/notifications/summary");
        var etag = first.Headers.ETag!.Tag;
        Row("Poll, nothing changed (304)", await Time(300, async () =>
        {
            var req = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/summary");
            req.Headers.TryAddWithoutValidation("If-None-Match", etag);
            var r = await c1.SendAsync(req);
            if (r.StatusCode != HttpStatusCode.NotModified) throw new Exception("expected 304");
        }), "< 20 ms");
        var miss = await Time(200, () => { tracker.TouchUser(principal.UserId()); _ = desk.Counts(principal); return Task.CompletedTask; });
        Row("Badge counts after a change (database)", miss, "< 20 ms");
        var deskT = await Time(60, async () => (await c1.GetAsync("/Desk")).EnsureSuccessStatusCode());
        Row("My Desk page", deskT, "< 250 ms");
        var notes = db.Strings("SELECT CAST(n.Id AS TEXT) FROM ENotes n JOIN ENoteParticipants p ON p.NoteId = n.Id AND p.UserId = @holder LIMIT 40", new { holder });
        var k = 0;
        var viewT = await Time(80, async () => (await c1.GetAsync("/ENotes/" + notes[k++ % notes.Count])).EnsureSuccessStatusCode());
        Row("Note view (10 movements, 2 comments)", viewT, "< 300 ms");
        Row("My e-Notes: Pending with me", await Time(40, async () => (await c1.GetAsync("/ENotes")).EnsureSuccessStatusCode()), "—");
        Row("My e-Notes: Seen by me, page 2", await Time(40, async () => (await c1.GetAsync("/ENotes?tab=seen&page=2")).EnsureSuccessStatusCode()), "—");
        Row("Approved notes, full-text search", await Time(40, async () => (await c1.GetAsync("/ENotes?tab=approved&q=watershed+budget")).EnsureSuccessStatusCode()), "—");
        Row("Notifications page 3", await Time(40, async () => (await c1.GetAsync("/Notifications?page=3")).EnsureSuccessStatusCode()), "—");
        var admin = await _app.SignIn("admin");
        Row("Register (all 20,000), page 1", await Time(30, async () => (await admin.GetAsync("/ENotes/Register")).EnsureSuccessStatusCode()), "—");
        Row("Register filtered + search", await Time(30, async () => (await admin.GetAsync("/ENotes/Register?state=InMovement&q=survey")).EnsureSuccessStatusCode()), "—");
        var xs = Stopwatch.StartNew();
        var x = await admin.GetAsync("/ENotes/Register?handler=Export");
        output.WriteLine($"Register Excel export (20,000 rows): {xs.ElapsedMilliseconds} ms, {(await x.Content.ReadAsByteArrayAsync()).Length / 1024} KB");

        // a marking step at volume (write path)
        var wf = _app.Get<ENoteWorkflow>();
        var pending = db.Strings("SELECT CAST(Id AS TEXT) FROM ENotes WHERE CurrentHolderUserId = @holder AND State = 'InMovement' LIMIT 20", new { holder }).Select(long.Parse).ToList();
        var to = users.First(u => u != holder);
        var markT = new List<double>();
        foreach (var id in pending)
        {
            var n = _app.Get<ENoteRepository>().Get(id)!;
            if (n.InitiatorUserId == to) continue;
            var s = Stopwatch.StartNew(); wf.MarkTo(id, to, "Bench marking", n.RowVersion, principal); markT.Add(s.Elapsed.TotalMilliseconds);
        }
        Row("Mark to (write, incl. audit entry)", Stats(markT), "—");

        Assert.True(miss.p95 < 20, "count budget");
        Assert.True(deskT.p95 < 250, "My Desk budget");
        Assert.True(viewT.p95 < 300, "note view budget");
    }
}
