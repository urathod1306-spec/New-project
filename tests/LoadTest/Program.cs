// NABCONS Intranet load test (test tool, not part of the package).
//   dotnet run -- seed <path-to-intranet.db>          adds the benchmark volume (intranet stopped)
//   dotnet run -- run <base-url> <minutes> <users>    drives virtual users and prints p50/p95 per operation
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Tests;

if (args.Length >= 2 && args[0] == "seed")
{
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(BenchSeed.Password, salt, 210_000, HashAlgorithmName.SHA256, 32);
    var stored = $"PBKDF2-SHA256$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    using var c = new SqliteConnection($"Data Source={args[1]}");
    c.Open();
    var sw = Stopwatch.StartNew();
    BenchSeed.All(c, stored);
    Console.WriteLine($"Seeded benchmark volume in {sw.Elapsed.TotalSeconds:0} s");
    return 0;
}
if (args.Length < 4 || args[0] != "run") { Console.WriteLine("usage: seed <db> | run <base-url> <minutes> <users>"); return 1; }

var baseUrl = args[1].TrimEnd('/');
var minutes = double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture);
var vus = int.Parse(args[3]);
var stats = new ConcurrentDictionary<string, ConcurrentBag<double>>();
var errors = new ConcurrentDictionary<string, int>();
var deadline = DateTime.UtcNow.AddMinutes(minutes);
var tokenRx = new Regex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");

async Task<HttpResponseMessage> Timed(string op, Func<Task<HttpResponseMessage>> call)
{
    var sw = Stopwatch.StartNew();
    HttpResponseMessage r;
    try { r = await call(); }
    catch (Exception) { errors.AddOrUpdate(op, 1, (_, n) => n + 1); throw; }
    stats.GetOrAdd(op, _ => new()).Add(sw.Elapsed.TotalMilliseconds);
    if ((int)r.StatusCode >= 500) errors.AddOrUpdate(op, 1, (_, n) => n + 1);
    return r;
}

async Task User(int n)
{
    var rnd = new Random(n * 7919);
    var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false, UseCookies = true };
    using var http = new HttpClient(handler) { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(60) };
    var me = "bench" + (n * 9 % 500);
    var login = await (await http.GetAsync("/Account/Login")).Content.ReadAsStringAsync();
    var tok = tokenRx.Match(login).Groups[1].Value;
    await Timed("sign-in", () => http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["Username"] = me, ["Password"] = BenchSeed.Password, ["__RequestVerificationToken"] = tok })));
    string etag = "";
    var nextPoll = DateTime.UtcNow.AddSeconds(rnd.Next(0, 60));
    while (DateTime.UtcNow < deadline)
    {
        try
        {
            if (DateTime.UtcNow >= nextPoll)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/summary");
                if (etag != "") req.Headers.TryAddWithoutValidation("If-None-Match", etag);
                var pr = await Timed("poll (summary)", () => http.SendAsync(req));
                if (pr.Headers.ETag != null) etag = pr.Headers.ETag.Tag;
                nextPoll = DateTime.UtcNow.AddSeconds(60);
            }
            var roll = rnd.Next(100);
            if (roll < 35) await Timed("My Desk", () => http.GetAsync("/Desk"));
            else if (roll < 65)
            {
                var list = await (await Timed("list: seen by me", () => http.GetAsync($"/ENotes?tab=seen&page={rnd.Next(1, 3)}"))).Content.ReadAsStringAsync();
                var ids = Regex.Matches(list, "href=\"/ENotes/(\\d+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
                if (ids.Count > 0) await Timed("note view", () => http.GetAsync("/ENotes/" + ids[rnd.Next(ids.Count)]));
            }
            else if (roll < 80)
            {
                var list = await (await http.GetAsync("/ENotes")).Content.ReadAsStringAsync();
                var id = Regex.Match(list, "href=\"/ENotes/(\\d+)\"").Groups[1].Value;
                if (id != "")
                {
                    var page = await (await Timed("note view", () => http.GetAsync("/ENotes/" + id))).Content.ReadAsStringAsync();
                    var t = tokenRx.Match(page).Groups[1].Value;
                    var rv = Regex.Match(page, "name=\"rowVersion\" value=\"(\\d+)\"").Groups[1].Value;
                    var people = await (await http.GetAsync("/api/users/lookup?q=Bench+User+" + rnd.Next(10, 49))).Content.ReadAsStringAsync();
                    var to = Regex.Matches(people, "\"id\":(\\d+)").Select(m => m.Groups[1].Value).OrderBy(_ => rnd.Next()).FirstOrDefault();
                    if (t != "" && rv != "" && to != null)
                        await Timed("mark to (POST)", () => http.PostAsync($"/ENotes/{id}?handler=Mark", new FormUrlEncodedContent(new Dictionary<string, string>
                            { ["__RequestVerificationToken"] = t, ["toUserId"] = to, ["toQuery"] = "", ["remarks"] = "Load test: please see.", ["rowVersion"] = rv })));
                }
            }
            else if (roll < 90) await Timed("search approved notes", () => http.GetAsync("/ENotes?tab=approved&q=" + new[] { "watershed", "budget", "survey", "vehicle" }[rnd.Next(4)]));
            else await Timed("Notifications page", () => http.GetAsync("/Notifications"));
        }
        catch (Exception) { /* counted */ }
        await Task.Delay(rnd.Next(1000, 4000));      // think time
    }
}

Console.WriteLine($"Running {vus} virtual users for {minutes} minutes against {baseUrl} ...");
var all = Stopwatch.StartNew();
await Task.WhenAll(Enumerable.Range(0, vus).Select(User));
Console.WriteLine($"\nFinished in {all.Elapsed.TotalMinutes:0.0} min. Requests: {stats.Values.Sum(b => b.Count):N0} ({stats.Values.Sum(b => b.Count) / all.Elapsed.TotalSeconds:0.0}/s)");
Console.WriteLine($"{"Operation",-24} {"count",7} {"p50 ms",8} {"p95 ms",8} {"max ms",8} {"5xx/err",8}");
foreach (var (op, bag) in stats.OrderBy(k => k.Key))
{
    var t = bag.OrderBy(x => x).ToList();
    Console.WriteLine($"{op,-24} {t.Count,7} {t[t.Count / 2],8:0.0} {t[(int)(t.Count * 0.95)],8:0.0} {t[^1],8:0.0} {errors.GetValueOrDefault(op),8}");
}
return errors.Values.Sum() > 0 ? 2 : 0;
