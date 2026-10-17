using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace NabconsIntranet.Tests;

/// <summary>
/// Starts the real application in memory with a fresh, throw-away data folder and the synthetic demo data
/// (demo accounts: admin, publisher, approver1, approver2, staff, auditor, bdu, coo — password Nabcons@2026).
/// </summary>
public sealed class TestApp : IDisposable
{
    public const string Password = "Nabcons@2026";
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "nabcons-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public TestApp()
    {
        Directory.CreateDirectory(Root);
        // Program reads configuration while building, so the test folders are passed as environment variables.
        Environment.SetEnvironmentVariable("Intranet__DataFolder", Path.Combine(Root, "data"));
        Environment.SetEnvironmentVariable("Intranet__DocumentsFolder", Path.Combine(Root, "documents"));
        Environment.SetEnvironmentVariable("Intranet__BackupFolder", Path.Combine(Root, "backups"));
        Environment.SetEnvironmentVariable("Intranet__LogFolder", Path.Combine(Root, "logs"));
        Environment.SetEnvironmentVariable("Intranet__DailyBackupTime", "off");
        _factory = new WebApplicationFactory<Program>();
        _ = _factory.Services; // start now, so the folders above are used
    }

    public T Get<T>() where T : notnull => _factory.Services.GetRequiredService<T>();

    public HttpClient NewClient() => _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public async Task<HttpClient> SignIn(string user, string password = Password)
    {
        var c = NewClient();
        var token = await Token(c, "/Account/Login");
        var r = await c.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Username"] = user, ["Password"] = password, ["__RequestVerificationToken"] = token,
        }));
        Assert.True(r.StatusCode == HttpStatusCode.Redirect, $"sign-in as {user} failed: {r.StatusCode}");
        return c;
    }

    /// <summary>Loads a page and returns the first anti-forgery token on it (optionally from the form whose action contains a fragment).</summary>
    public static async Task<string> Token(HttpClient c, string url, string? formActionContains = null)
    {
        var html = await c.GetStringAsync(url);
        if (formActionContains != null)
        {
            var i = html.IndexOf(formActionContains, StringComparison.Ordinal);
            Assert.True(i >= 0, $"form '{formActionContains}' not found on {url}");
            html = html[i..];
        }
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"");
        Assert.True(m.Success, "no anti-forgery token on " + url);
        return m.Groups[1].Value;
    }

    public static FormUrlEncodedContent Form(string token, params (string k, string v)[] fields)
    {
        var d = fields.ToDictionary(f => f.k, f => f.v);
        d["__RequestVerificationToken"] = token;
        return new FormUrlEncodedContent(d);
    }

    public AppUser User(string username) => Get<UserRepository>().GetByUsername(username)!;

    public ClaimsPrincipal Principal(string username) =>
        new(IdentityBuilder.BuildIdentity(User(username), "test"));

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Root, true); } catch { /* best effort */ }
    }
}
