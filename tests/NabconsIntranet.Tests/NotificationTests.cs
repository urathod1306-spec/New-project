using System.Net;
using System.Text.Json;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Tests;

public class NotificationTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private INotificationService Notes => _app.Get<INotificationService>();
    private Db Db => _app.Get<Db>();
    private static NotificationMessage Msg(string title = "Test") => new("Request.Test", title, null, "/Requests", "Request", 1);

    private long SubmitRequest(string submitter = "staff")
    {
        var wf = _app.Get<RequestWorkflow>();
        var me = _app.Principal(submitter);
        var id = wf.CreateDraft(new ApprovalRequest
        {
            Subject = "Purchase of a printer", RequestCategory = "Procurement", Details = "Needed for the RID team.",
            ReviewerUserId = _app.User("approver1").Id, ApproverUserId = _app.User("approver2").Id,
        }, me);
        wf.Execute(id, "Submit", null, wf.Get(id)!.RowVersion, me);
        return id;
    }

    [Fact]
    public void Notification_is_part_of_the_action_transaction()
    {
        var staff = _app.User("staff").Id;
        var before = Notes.UnreadCount(staff);
        using (var c = Db.Open())
        using (var tx = c.BeginTransaction())
        {
            Assert.Equal(1, Notes.Notify(c, tx, new[] { staff }, new NotificationActor(_app.User("admin").Id, "Admin"), Msg()));
            tx.Rollback();
        }
        Assert.Equal(before, Notes.UnreadCount(staff));          // rolled back with the action
        using (var c = Db.Open())
        using (var tx = c.BeginTransaction())
        {
            Notes.Notify(c, tx, new[] { staff }, new NotificationActor(_app.User("admin").Id, "Admin"), Msg());
            tx.Commit();
        }
        Assert.Equal(before + 1, Notes.UnreadCount(staff));

        // and through a real workflow step: submitting assigns the request to the reviewer
        var reviewer = _app.User("approver1").Id;
        var r0 = Notes.UnreadCount(reviewer);
        var id = SubmitRequest();
        var n = Notes.Latest(reviewer, 1).Single();
        Assert.Equal(r0 + 1, Notes.UnreadCount(reviewer));
        Assert.Equal("Request.Assigned", n.Type);
        Assert.Equal($"/Requests/Details/{id}", n.Link);
    }

    [Fact]
    public void Nobody_is_notified_about_their_own_action()
    {
        var admin = _app.User("admin");
        using (var c = Db.Open())
        using (var tx = c.BeginTransaction())
        {
            Assert.Equal(0, Notes.Notify(c, tx, new[] { admin.Id }, new NotificationActor(admin.Id, admin.DisplayName), Msg()));
            // the administrators' broadcast skips the administrator who acts
            var others = Notes.NotifyAdministrators(c, tx, new NotificationActor(admin.Id, admin.DisplayName), Msg());
            tx.Commit();
            Assert.Equal(0, Db.Scalar<long>("SELECT COUNT(*) FROM Notifications WHERE UserId = @id AND Title = 'Test'", new { id = admin.Id }));
            Assert.True(others >= 0);
        }
        // approver1 recommends: approver2 and the submitter are told, approver1 is not
        var id = SubmitRequest();
        var wf = _app.Get<RequestWorkflow>();
        var a1 = _app.User("approver1").Id;
        var before = Notes.UnreadCount(a1);
        wf.Execute(id, "Recommend", "Fine", wf.Get(id)!.RowVersion, _app.Principal("approver1"));
        Assert.Equal(before, Notes.UnreadCount(a1));
        Assert.Contains(Notes.Latest(_app.User("approver2").Id, 5), x => x.Type == "Request.Assigned" && x.EntityId == id);
        Assert.Contains(Notes.Latest(_app.User("staff").Id, 5), x => x.Type == "Request.Recommended" && x.EntityId == id);
    }

    [Fact]
    public void Links_must_stay_inside_the_intranet()
    {
        foreach (var bad in new[] { "//evil.example/x", "http://evil.example", "/\\evil", "javascript:alert(1)", "Requests/1", "/a\nb" })
            Assert.False(NotificationService.IsSafeLink(bad), bad);
        Assert.True(NotificationService.IsSafeLink("/Requests/Details/5?x=1"));
        using var c = Db.Open();
        using var tx = c.BeginTransaction();
        Assert.Throws<ArgumentException>(() => Notes.Notify(c, tx, new[] { 1L }, new NotificationActor(0, "x"), Msg() with { Link = "//evil.example" }));
    }

    [Fact]
    public async Task Users_cannot_read_count_or_mark_someone_elses_notifications()
    {
        SubmitRequest();
        var reviewer = _app.User("approver1").Id;
        var theirs = Notes.Latest(reviewer, 1).Single();
        var staff = await _app.SignIn("staff");
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/Notifications/Open/{theirs.Id}")).StatusCode);
        Assert.Null(Notes.Latest(reviewer, 1).Single().ReadAt);
        Assert.DoesNotContain(theirs.Title, await staff.GetStringAsync("/Notifications"));
        Assert.DoesNotContain(theirs.Title, await staff.GetStringAsync("/Notifications?handler=Menu"));
        var token = await TestApp.Token(staff, "/Notifications");
        await staff.PostAsync("/Notifications?handler=MarkAllRead", TestApp.Form(token));
        Assert.True(Notes.UnreadCount(reviewer) > 0);               // staff's "mark all read" leaves approver1's untouched
        var json = JsonDocument.Parse(await staff.GetStringAsync("/api/notifications/summary")).RootElement;
        Assert.Equal(0, json.GetProperty("unread").GetInt64());

        // the owner opens it: marked read and sent to the request
        var a1 = await _app.SignIn("approver1");
        var r = await a1.GetAsync($"/Notifications/Open/{theirs.Id}");
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        Assert.Equal(theirs.Link, r.Headers.Location!.ToString());
        Assert.NotNull(Notes.Latest(reviewer, 1).Single().ReadAt);
    }

    [Fact]
    public async Task Poll_answers_304_when_nothing_changed_and_200_after_a_change()
    {
        var a1 = await _app.SignIn("approver1");
        var first = await a1.GetAsync("/api/notifications/summary");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var etag = first.Headers.ETag!.Tag;
        var again = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/summary");
        again.Headers.TryAddWithoutValidation("If-None-Match", etag);
        Assert.Equal(HttpStatusCode.NotModified, (await a1.SendAsync(again)).StatusCode);

        SubmitRequest();                                             // assigns a request to approver1
        await Task.Delay(1100);
        var after = new HttpRequestMessage(HttpMethod.Get, "/api/notifications/summary");
        after.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var changed = await a1.SendAsync(after);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var json = JsonDocument.Parse(await changed.Content.ReadAsStringAsync()).RootElement;
        Assert.True(json.GetProperty("unread").GetInt64() >= 1);
        Assert.True(json.GetProperty("inbox").GetInt64() >= 1);

        // signed out: 401, not a redirect to the sign-in page
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.NewClient().GetAsync("/api/notifications/summary")).StatusCode);
    }

    [Fact]
    public void Housekeeping_removes_old_read_and_very_old_unread_notifications_only()
    {
        var uid = _app.User("staff").Id;
        var now = DateTime.UtcNow;
        void Add(string title, int ageDays, bool read) => Db.Exec(
            "INSERT INTO Notifications (UserId, Type, Title, CreatedAt, ReadAt) VALUES (@uid, 'System.Test', @title, @at, @readAt)",
            new { uid, title, at = now.AddDays(-ageDays).ToString("o"), readAt = read ? now.AddDays(-ageDays).ToString("o") : null });
        Add("read-old", 91, true); Add("read-recent", 30, true); Add("unread-89", 89, false); Add("unread-old", 366, false); Add("unread-300", 300, false);
        var result = _app.Get<HousekeepingService>().Run();
        var left = Db.Strings("SELECT Title FROM Notifications WHERE Type = 'System.Test' ORDER BY Title");
        Assert.Equal(new[] { "read-recent", "unread-300", "unread-89" }, left);
        Assert.Equal(1, result.ReadDeleted);
        Assert.Equal(1, result.UnreadDeleted);
        Assert.True(_app.Get<AuditService>().Verify().ok);
    }

    [Fact]
    public async Task My_Desk_sections_follow_roles_and_the_sign_in_preference()
    {
        SubmitRequest();
        var staff = await _app.SignIn("staff");
        var html = await staff.GetStringAsync("/Desk");
        Assert.DoesNotContain("Self-registrations to approve", html);
        Assert.DoesNotContain("Requests awaiting my action", html);
        Assert.DoesNotContain("Leads in “Submitted”", html);
        Assert.Contains("Recent notifications", html);

        var a1 = await _app.SignIn("approver1");
        html = await a1.GetStringAsync("/Desk");
        Assert.Contains("Requests awaiting my action", html);
        Assert.Contains("Purchase of a printer", html);

        var admin = await _app.SignIn("admin");
        Assert.Contains("Self-registrations to approve", await admin.GetStringAsync("/Desk"));
        var bdu = await _app.SignIn("bdu");
        Assert.Contains("Leads in “Submitted”", await bdu.GetStringAsync("/Desk"));

        // default: sign-in opens My Desk; switched off: the home page
        var c = _app.NewClient();
        var token = await TestApp.Token(c, "/Account/Login");
        var r = await c.PostAsync("/Account/Login", TestApp.Form(token, ("Username", "staff"), ("Password", TestApp.Password)));
        Assert.Equal("/Desk", r.Headers.Location!.ToString());
        _app.Get<UserRepository>().SetOpenDeskAfterSignIn(_app.User("staff").Id, false);
        c = _app.NewClient();
        token = await TestApp.Token(c, "/Account/Login");
        r = await c.PostAsync("/Account/Login", TestApp.Form(token, ("Username", "staff"), ("Password", TestApp.Password)));
        Assert.Equal("/", r.Headers.Location!.ToString());
    }

    [Fact]
    public void Leads_registrations_and_vertical_changes_notify_the_right_people()
    {
        var leads = _app.Get<LeadRepository>();
        var bdu = _app.User("bdu").Id;
        var lead = leads.Create(new Lead { Title = "Evaluation study", ClientName = "State Mission", ClientType = "State Govt" }, _app.Principal("staff"));
        Assert.Contains(Notes.Latest(bdu, 3), n => n.Type == "Lead.Submitted" && n.EntityId == lead.Id);
        leads.ChangeStatus(leads.Get(lead.Id)!, new LeadUpdate(LeadStatus.UnderReview, "Checking eligibility", null, null, null, null), leads.Get(lead.Id)!.RowVersion, _app.Principal("bdu"));
        Assert.Contains(Notes.Latest(_app.User("staff").Id, 3), n => n.Type == "Lead.StatusChanged");
        Assert.DoesNotContain(Notes.Latest(bdu, 3), n => n.Type == "Lead.StatusChanged");   // the actor

        _app.Get<RegistrationRepository>().Submit(new RegistrationRequest { Mobile = "9812345670", FullName = "New Joiner" });
        Assert.Contains(Notes.Latest(_app.User("admin").Id, 3), n => n.Type == "Admin.RegistrationPending");
        var staffUser = _app.User("staff");
        _app.Get<ProfileRepository>().RequestVertical(staffUser, null, "BDU");
        Assert.Contains(Notes.Latest(_app.User("admin").Id, 3), n => n.Type == "Admin.VerticalChangePending");
    }
}

/// <summary>Separate app with a 1-minute session, so the sliding-expiration half-life is reachable in a test.</summary>
public class PollSessionTests : IDisposable
{
    private readonly TestApp _app = new(("Intranet__SessionTimeoutMinutes", "1"));
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Polling_does_not_extend_the_session_but_a_page_view_does()
    {
        var c = await _app.SignIn("staff");
        await Task.Delay(TimeSpan.FromSeconds(32));                  // past half of the 1-minute session
        var poll = await c.GetAsync("/api/notifications/summary");
        Assert.Equal(HttpStatusCode.OK, poll.StatusCode);
        Assert.False(poll.Headers.TryGetValues("Set-Cookie", out var pc) && pc.Any(v => v.StartsWith("NABCONS.Auth", StringComparison.Ordinal)),
            "the poll must not renew the sign-in cookie");
        var page = await c.GetAsync("/Directory");
        Assert.True(page.Headers.TryGetValues("Set-Cookie", out var sc) && sc.Any(v => v.StartsWith("NABCONS.Auth", StringComparison.Ordinal)),
            "a real page view renews the sign-in cookie");
    }
}
