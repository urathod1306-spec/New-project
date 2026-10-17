using System.Net;

namespace NabconsIntranet.Tests;

/// <summary>Smoke tests that v1.2 did not change existing behaviour.</summary>
public class ExistingBehaviourTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Sign_in_pages_and_permissions_work_as_before()
    {
        Assert.Equal(HttpStatusCode.Redirect, (await _app.NewClient().GetAsync("/Circulars")).StatusCode);
        var staff = await _app.SignIn("staff");
        foreach (var url in new[] { "/", "/Directory?q=consultant", "/Circulars?q=leave", "/Requests", "/Account/ChangePassword", "/Profile", "/Leads/Mine", "/Leads/New" })
            Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await staff.GetAsync("/Admin")).StatusCode); // -> access denied, as before
        var admin = await _app.SignIn("admin");
        foreach (var url in new[] { "/Admin", "/Admin/Users", "/Admin/Masters?type=ClientType", "/Admin/Registrations", "/Admin/ProfileChanges", "/Audit" })
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
        var (ok, count, _) = _app.Get<NabconsIntranet.Infrastructure.AuditService>().Verify();
        Assert.True(ok);
        Assert.True(count > 10);
    }

    [Fact]
    public async Task Wrong_password_still_fails_and_menu_shows_my_profile_above_change_password()
    {
        var c = _app.NewClient();
        var token = await TestApp.Token(c, "/Account/Login");
        var r = await c.PostAsync("/Account/Login", TestApp.Form(token, ("Username", "staff"), ("Password", "wrong")));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var html = await (await _app.SignIn("staff")).GetStringAsync("/");
        Assert.True(html.IndexOf(">My Profile<", StringComparison.Ordinal) < html.IndexOf(">Change password<", StringComparison.Ordinal));
        Assert.True(html.IndexOf(">My Profile<", StringComparison.Ordinal) > 0);
    }
}
