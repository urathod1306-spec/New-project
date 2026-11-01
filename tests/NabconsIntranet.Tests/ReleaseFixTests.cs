using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using SkiaSharp;

namespace NabconsIntranet.Tests;

/// <summary>Release 1.5.0: fixes for the findings of the Elephant–Goldfish design review (docs/designs/reviews).</summary>
public class ReleaseFixTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static IFormFile Upload(string name, byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photos", name) { Headers = new HeaderDictionary(), ContentType = "application/octet-stream" };

    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.SeaGreen);
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public void Failed_sign_ins_are_counted_exactly_when_they_arrive_together()   // P5
    {
        var users = _app.Get<UserRepository>();
        var u = _app.User("staff");
        Parallel.For(0, 4, _ => users.RecordFailedLogin(users.GetByUsername("staff")!, 5, 15));
        Assert.Equal(4, users.GetByUsername("staff")!.FailedLogins);
        users.RecordFailedLogin(u, 5, 15);                         // a stale copy (count 0) still adds to the real count: the fifth locks
        var after = users.GetByUsername("staff")!;
        Assert.True(after.IsLockedOut);
        Assert.Equal(0, after.FailedLogins);                       // the count starts again after a lock-out
    }

    [Fact]
    public async Task Unknown_user_name_and_wrong_password_get_the_same_answer()        // P6
    {
        async Task<string> Try(string user)
        {
            var c = _app.NewClient();
            var token = await TestApp.Token(c, "/Account/Login");
            var r = await c.PostAsync("/Account/Login", TestApp.Form(token, ("Username", user), ("Password", "Wrong-password-1")));
            return await r.Content.ReadAsStringAsync();
        }
        var unknown = await Try("nobody-here");
        var wrong = await Try("approver2");
        Assert.Contains("The user name or password is incorrect.", unknown);
        Assert.Contains("The user name or password is incorrect.", wrong);
    }

    [Fact]
    public void Backup_is_checked_before_it_is_kept_and_leaves_no_partial_file()      // P2, P3
    {
        var b = _app.Get<BackupService>().Create(_app.Get<AuditService>(), "test");
        Assert.True(File.Exists(b.FullPath));
        var folder = Path.GetDirectoryName(b.FullPath)!;
        Assert.Empty(Directory.GetFiles(folder, "*.partial"));
        using var zip = ZipFile.OpenRead(b.FullPath);
        Assert.NotNull(zip.GetEntry("data/intranet.db"));
    }

    [Fact]
    public void Restore_puts_the_backup_in_place_and_keeps_the_old_data_aside()      // P4
    {
        var backup = _app.Get<BackupService>().Create(_app.Get<AuditService>(), "test");
        var root = Path.Combine(_app.Root, "restore-target");
        var o = new IntranetOptions { DataFolder = Path.Combine(root, "data"), DocumentsFolder = Path.Combine(root, "documents"),
                                      BackupFolder = Path.Combine(root, "backups"), LogFolder = Path.Combine(root, "logs") };
        o.Resolve(root);
        File.WriteAllText(Path.Combine(o.DataPath, "marker.txt"), "old live data");

        var output = new StringWriter();
        Assert.Equal(0, BackupService.Restore(o, backup.FullPath, output));
        Assert.True(File.Exists(o.DatabasePath));
        Assert.False(File.Exists(Path.Combine(o.DataPath, "marker.txt")));
        var aside = Directory.GetDirectories(root, "data.before-restore-*").Single();
        Assert.True(File.Exists(Path.Combine(aside, "marker.txt")));               // never deleted
        Assert.Empty(Directory.GetDirectories(root, "*.restoring-*"));             // staging cleaned up
        Assert.Contains("appsettings.json is not restored", output.ToString());

        // a ZIP that is not a backup changes nothing
        var notBackup = Path.Combine(root, "other.zip");
        using (var z = ZipFile.Open(notBackup, ZipArchiveMode.Create)) z.CreateEntry("readme.txt");
        Assert.Equal(4, BackupService.Restore(o, notBackup, new StringWriter()));
        Assert.True(File.Exists(o.DatabasePath));
    }

    [Fact]
    public async Task People_lookup_is_off_when_e_note_is_off()                       // D1
    {
        using var off = new TestApp(("ENote__Enabled", "false"));
        var c = await off.SignIn("staff");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/users/lookup?q=raj")).StatusCode);
        var on = await _app.SignIn("staff");
        Assert.Equal(HttpStatusCode.OK, (await on.GetAsync("/api/users/lookup?q=raj")).StatusCode);
    }

    [Fact]
    public async Task Duplicate_check_shows_details_only_of_leads_the_person_may_see()   // M9
    {
        var bdu = await _app.SignIn("bdu");
        var token = await TestApp.Token(bdu, "/Leads/New");
        using var form = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" },
            { new StringContent("Impact study of dairy cooperatives"), "Input.Title" },
            { new StringContent("State Dairy Federation"), "Input.ClientName" },
            { new StringContent("State Govt"), "Input.ClientType" },
            { new StringContent("Impact study."), "Input.Description" },
            { new StringContent("true"), "ConfirmDuplicate" },
        };
        Assert.Equal(HttpStatusCode.Redirect, (await bdu.PostAsync("/Leads/New", form)).StatusCode);
        var url = "/Leads/New?handler=Check&client=" + Uri.EscapeDataString("State Dairy Federation") + "&title=" + Uri.EscapeDataString("Dairy cooperatives impact study");

        var staffJson = JsonDocument.Parse(await (await _app.SignIn("staff")).GetStringAsync(url)).RootElement;
        Assert.True(staffJson.GetArrayLength() > 0);                                         // still warned...
        Assert.All(staffJson.EnumerateArray(), d => Assert.Equal(JsonValueKind.Null, d.GetProperty("reference").ValueKind));   // ...without the details

        var bduJson = JsonDocument.Parse(await bdu.GetStringAsync(url)).RootElement;
        Assert.Contains(bduJson.EnumerateArray(), d => d.GetProperty("reference").ValueKind == JsonValueKind.String);
    }

    [Fact]
    public void Failed_posts_do_not_use_up_the_hourly_limit_and_leave_no_files()      // D12, D16
    {
        var b = _app.Get<BoardRepository>();
        var p = _app.Principal("approver2");
        int Files() => Directory.Exists(b.Folder) ? Directory.GetFiles(b.Folder, "*", SearchOption.AllDirectories).Length : 0;
        var before = Files();
        for (var i = 0; i < 12; i++)
            Assert.Throws<WorkflowException>(() => b.Create("bad", new[] { Upload("ok.png", Png(64, 64)), Upload("evil.jpg", new byte[] { 0x4D, 0x5A, 1, 2 }) }, p));
        Assert.Equal(before, Files());                                                  // the good photo written first was removed
        Assert.True(b.Create("good", new[] { Upload("ok.png", Png(64, 64)) }, p) > 0);  // twelve failures did not use up the ten-an-hour limit
        Assert.Equal(before + 2, Files());                                              // photo and thumbnail
    }

    [Fact]
    public void A_removed_post_cannot_be_pinned()                                       // D17
    {
        var b = _app.Get<BoardRepository>();
        var id = b.Create("to remove", Array.Empty<IFormFile>(), _app.Principal("staff"));
        b.Delete(id, null, _app.Principal("admin"));
        Assert.Throws<WorkflowException>(() => b.SetPinned(id, true, _app.Principal("admin")));
        Assert.False(b.Get(id)!.IsPinned);
    }

    [Fact]
    public void Orphan_report_covers_every_module()                                     // Z1, Z16
    {
        var hk = _app.Get<HousekeepingService>();
        Assert.Empty(hk.OrphanFiles(TimeSpan.Zero));
        var o = _app.Get<IntranetOptions>();
        var stray = Path.Combine(o.DocumentsPath, "requests", "2026", "stray.dat");
        Directory.CreateDirectory(Path.GetDirectoryName(stray)!);
        File.WriteAllText(stray, "left over");
        Assert.Single(hk.OrphanFiles(TimeSpan.Zero));
        Assert.Empty(hk.OrphanFiles());                                             // files from the last hour are left alone
        Assert.True(File.Exists(stray));                                            // reported, never deleted
    }

    [Fact]
    public void Retired_directory_entries_are_not_matched_on_registration()           // M10
    {
        var staff = _app.Get<StaffRepository>();
        var reg = _app.Get<RegistrationRepository>();
        var s = staff.All().First(x => !string.IsNullOrEmpty(x.Mobile) && RegistrationRepository.Normalize(x.Mobile) != null);
        var mobile = RegistrationRepository.Normalize(s.Mobile)!;
        Assert.NotNull(reg.FindStaffByMobile(mobile));
        s.ActiveTo = DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd");
        staff.Upsert(s, "test");
        Assert.Null(reg.FindStaffByMobile(mobile));
    }

    [Fact]
    public void Two_approvals_of_one_registration_create_one_account()                 // M4
    {
        var reg = _app.Get<RegistrationRepository>();
        var id = reg.Submit(new RegistrationRequest { Mobile = "9876501234", FullName = "Asha Verma", PasswordHash = PasswordHasher.Hash(TestApp.Password) });
        var r = reg.Get(id)!;
        reg.Approve(r, new[] { Roles.Staff }, false, null, "admin");
        var again = Assert.Throws<WorkflowException>(() => reg.Approve(reg.Get(id)!, new[] { Roles.Staff }, false, null, "admin2"));
        Assert.Contains("already", again.Message);
        var done = reg.Get(id)!;
        Assert.Equal(RegistrationStatus.Approved, done.Status);
        Assert.NotNull(done.CreatedUserId);
        Assert.Null(done.PasswordHash);
    }

    [Fact]
    public void Closed_lead_statuses_come_from_one_list()                              // Z11
    {
        Assert.Contains("'Dropped / Not Pursued'", LeadRepository.ClosedSql);
        Assert.Equal(LeadStatus.Closed.Length, LeadRepository.ClosedSql.Split(", ").Length);
    }
}
