using System.Net;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Tests;

public class ProfileTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private StaffMember StaffOf(string user) => _app.Get<ProfileRepository>().StaffFor(_app.User(user))!;

    [Fact]
    public async Task User_edits_own_contact_details_which_reach_the_directory_and_the_audit_trail()
    {
        var c = await _app.SignIn("bdu");
        var token = await TestApp.Token(c, "/Profile?edit=true", "handler=Contact");
        var r = await c.PostAsync("/Profile?handler=Contact", TestApp.Form(token,
            ("Mobile", "+91 98765 00011"), ("Extension", "2299"), ("OfficialEmail", "rohan.k@nabcons.local"), ("Location", "Kohima")));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        var s = StaffOf("bdu");
        Assert.Equal("9876500011", s.Mobile);           // normalised
        Assert.Equal("2299", s.Extension);
        Assert.Equal("rohan.k@nabcons.local", s.OfficialEmail);
        Assert.Equal("Kohima", s.Location);
        var history = _app.Get<ProfileRepository>().History(_app.User("bdu").Id);
        Assert.Contains(history, h => h.Field == "Mobile" && h.NewValue == "9876500011" && h.ChangedBy == "bdu");
        Assert.Contains(history, h => h.Field == "Location" && h.OldValue == "Mumbai (Head Office)" && h.NewValue == "Kohima");
        Assert.Contains("Kohima", await c.GetStringAsync("/Directory?q=Rohan"));
    }

    [Fact]
    public async Task Invalid_or_duplicate_mobile_is_rejected()
    {
        var c = await _app.SignIn("bdu");
        var t1 = await TestApp.Token(c, "/Profile?edit=true", "handler=Contact");
        var html1 = await (await c.PostAsync("/Profile?handler=Contact", TestApp.Form(t1, ("Mobile", "12345")))).Content.ReadAsStringAsync();
        Assert.Contains("valid 10-digit", html1);

        // a mobile number already used by another ACTIVE user (here: the "staff" account's directory entry) is refused
        var other = StaffOf("staff");
        other.Mobile = "9811122233";
        _app.Get<StaffRepository>().Upsert(other, "test");
        var t2 = await TestApp.Token(c, "/Profile?edit=true", "handler=Contact");
        var html2 = await (await c.PostAsync("/Profile?handler=Contact", TestApp.Form(t2, ("Mobile", "98111 22233")))).Content.ReadAsStringAsync();
        Assert.Contains("already used by another active user", html2);
        Assert.NotEqual("9811122233", StaffOf("bdu").Mobile);
    }

    [Fact]
    public async Task Crafted_request_cannot_edit_another_user_or_name_designation_roles()
    {
        var victimBefore = StaffOf("staff");
        var c = await _app.SignIn("bdu");
        var token = await TestApp.Token(c, "/Profile?edit=true", "handler=Contact");
        var r = await c.PostAsync("/Profile?handler=Contact", TestApp.Form(token,
            ("Mobile", "9876500012"), ("Extension", "1"), ("OfficialEmail", "x@nabcons.local"), ("Location", "Kohima"),
            ("UserId", _app.User("staff").Id.ToString()), ("StaffId", victimBefore.StaffId.ToString()), ("Id", _app.User("staff").Id.ToString()),
            ("FullName", "Hacker"), ("DisplayName", "Hacker"), ("Designation", "Managing Director"), ("EmployeeCode", "X1"),
            ("Roles", "Staff,Administrator,LeadViewer"), ("Username", "admin")));
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);

        var victimAfter = StaffOf("staff");
        Assert.Equal(victimBefore.Mobile, victimAfter.Mobile);
        Assert.Equal(victimBefore.OfficialEmail, victimAfter.OfficialEmail);
        var me = _app.User("bdu");
        var myStaff = StaffOf("bdu");
        Assert.Equal("Rohan Kapoor", me.DisplayName);
        Assert.Equal("Rohan Kapoor", myStaff.FullName);
        Assert.Equal("Manager - Business Development", myStaff.Designation);
        Assert.Equal("NC1026", myStaff.EmployeeCode);
        Assert.DoesNotContain(Roles.Administrator, me.RoleList);
        Assert.DoesNotContain(Roles.LeadViewer, me.RoleList);
        Assert.Equal("bdu", me.Username);
        Assert.Equal("9876500012", myStaff.Mobile); // only the own allowed fields changed
    }

    [Fact]
    public async Task Signing_in_with_mobile_follows_a_mobile_change()
    {
        var users = _app.Get<UserRepository>();
        var u = _app.User("staff");
        var s = StaffOf("staff");
        // make "staff" a mobile-number sign-in, as self-registered users are
        Get<Db>().Exec("UPDATE Users SET Username = '9000002222' WHERE Id = @id", new { id = u.Id });
        var c = await _app.SignIn("9000002222");
        var token = await TestApp.Token(c, "/Profile?edit=true", "handler=Contact");
        await c.PostAsync("/Profile?handler=Contact", TestApp.Form(token, ("Mobile", "9000003333"), ("Location", s.Location ?? "")));
        Assert.NotNull(users.GetByUsername("9000003333"));
        Assert.Null(users.GetByUsername("9000002222"));
        await _app.SignIn("9000003333");
    }

    private T Get<T>() where T : notnull => _app.Get<T>();

    [Fact]
    public async Task Vertical_change_waits_for_approval_and_access_changes_only_after_it()
    {
        var c = await _app.SignIn("staff");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/Leads")).StatusCode);

        var token = await TestApp.Token(c, "/Profile", "handler=Vertical");
        await c.PostAsync("/Profile?handler=Vertical", TestApp.Form(token, ("Vertical", "BDU")));
        Assert.Equal("Rural Infrastructure", _app.User("staff").Vertical);              // unchanged
        Assert.Contains("Vertical change pending approval", await c.GetStringAsync("/Profile"));
        Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/Leads")).StatusCode); // still no BDU access

        var pending = Get<ProfileRepository>().Requests("Pending").Single();
        var admin = await _app.SignIn("admin");
        var t2 = await TestApp.Token(admin, "/Admin/ProfileChanges", "handler=Decide");
        await admin.PostAsync($"/Admin/ProfileChanges?handler=Decide&id={pending.Id}", TestApp.Form(t2, ("approve", "true")));

        Assert.Equal("BDU", _app.User("staff").Vertical);
        Assert.Equal("BDU", StaffOf("staff").Vertical);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/Leads")).StatusCode);       // same session, access now granted
    }

    [Fact]
    public async Task Admin_can_reject_vertical_change_only_with_remarks()
    {
        var c = await _app.SignIn("staff");
        var token = await TestApp.Token(c, "/Profile", "handler=Vertical");
        await c.PostAsync("/Profile?handler=Vertical", TestApp.Form(token, ("Vertical", "BDU")));
        var id = Get<ProfileRepository>().Requests("Pending").Single().Id;
        Assert.Throws<WorkflowException>(() => Get<ProfileRepository>().Decide(id, false, "", "admin"));
        Get<ProfileRepository>().Decide(id, false, "Not moving", "admin");
        Assert.Equal("Rural Infrastructure", _app.User("staff").Vertical);
    }

    [Fact]
    public void Profile_audit_is_append_only()
    {
        var db = Get<Db>();
        db.Exec("INSERT INTO ProfileAudit (UserId, Field, OldValue, NewValue, ChangedBy, ChangedAt) VALUES (1, 'X', 'a', 'b', 't', 'now')");
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() => db.Exec("UPDATE ProfileAudit SET NewValue = 'c'"));
        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() => db.Exec("DELETE FROM ProfileAudit"));
    }
}
