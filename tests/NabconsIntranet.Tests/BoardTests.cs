using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using SkiaSharp;

namespace NabconsIntranet.Tests;

public class BoardTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private BoardRepository B => _app.Get<BoardRepository>();

    private static byte[] Asset(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", name));

    private static IFormFile Upload(string name, byte[] bytes) =>
        new FormFile(new MemoryStream(bytes), 0, bytes.Length, "photos", name) { Headers = new HeaderDictionary(), ContentType = "application/octet-stream" };

    private static byte[] Png(int w, int h)
    {
        using var bmp = new SKBitmap(w, h);
        using (var c = new SKCanvas(bmp)) c.Clear(SKColors.SeaGreen);
        using var img = SKImage.FromBitmap(bmp);
        return img.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private List<string> Files() => Directory.Exists(B.Folder) ? Directory.GetFiles(B.Folder, "*", SearchOption.AllDirectories).ToList() : new();

    [Fact]
    public void Anyone_can_post_text_and_photos_and_photos_are_cleaned()
    {
        var staff = _app.Principal("staff");
        var id = B.Create("Team outing photos\nfrom Lucknow", new[] { Upload("trip.jpg", Asset("rotated_gps.jpg")), Upload("big.png", Png(3000, 1000)) }, staff);
        var p = B.Get(id)!;
        Assert.Equal("Team outing photos\nfrom Lucknow", p.Body);
        Assert.Equal("Vikram Singh", p.AuthorName);
        Assert.Equal(2, p.Photos.Count);
        var first = File.ReadAllBytes(B.PhotoPath(p.Photos[0], false));
        Assert.DoesNotContain("Exif", System.Text.Encoding.Latin1.GetString(first));       // location and camera data removed
        using (var decoded = SKBitmap.Decode(first)) Assert.True(decoded.Height > decoded.Width, "turned upright from the EXIF orientation");
        Assert.Equal(BoardOptions.FullSide, p.Photos[1].Width);                              // large photo scaled down to fit 1600 px
        Assert.True(File.Exists(B.PhotoPath(p.Photos[1], true)));
        Assert.Contains(B.Page(1).Items, x => x.Id == id);
        // a photo-only post is fine too
        Assert.True(B.Create("", new[] { Upload("a.png", Png(400, 300)) }, _app.Principal("approver1")) > 0);
    }

    [Fact]
    public void Bad_posts_are_refused_and_leave_no_files()
    {
        var staff = _app.Principal("staff");
        Assert.Throws<WorkflowException>(() => B.Create("   ", Array.Empty<IFormFile>(), staff));
        Assert.Throws<WorkflowException>(() => B.Create(new string('x', 2001), Array.Empty<IFormFile>(), staff));
        Assert.Throws<WorkflowException>(() => B.Create("five", Enumerable.Range(0, 5).Select(i => Upload($"{i}.png", Png(64, 64))).ToArray(), staff));
        var before = Files().Count;
        var ex = Assert.Throws<WorkflowException>(() => B.Create("with a fake", new[] { Upload("ok.png", Png(64, 64)), Upload("evil.jpg", new byte[] { 0x4D, 0x5A, 1, 2, 3 }) }, staff));
        Assert.Contains("evil.jpg", ex.Message);
        Assert.Equal(before, Files().Count);                                                   // the good photo written first was removed again
        Assert.Throws<WorkflowException>(() => B.Create("too big", new[] { Upload("big.png", new byte[6 * 1024 * 1024]) }, staff));
    }

    [Fact]
    public void At_most_ten_posts_per_person_per_hour()
    {
        var p = _app.Principal("approver2");
        for (var i = 0; i < 10; i++) B.Create($"Post {i}", Array.Empty<IFormFile>(), p);
        var ex = Assert.Throws<WorkflowException>(() => B.Create("one too many", Array.Empty<IFormFile>(), p));
        Assert.Contains("10 messages an hour", ex.Message);
        B.Create("someone else can still post", Array.Empty<IFormFile>(), _app.Principal("auditor"));
    }

    [Fact]
    public void Only_administrators_pin_and_at_most_three()
    {
        var admin = _app.Principal("admin");
        var ids = Enumerable.Range(1, 4).Select(i => B.Create($"Post {i}", Array.Empty<IFormFile>(), _app.Principal("staff"))).ToList();
        Assert.Throws<WorkflowException>(() => B.SetPinned(ids[0], true, _app.Principal("staff")));
        var already = B.Pinned().Count;                                                         // the demo data pins one post
        foreach (var id in ids.Take(3 - already)) B.SetPinned(id, true, admin);
        Assert.Equal(3, B.Pinned().Count);
        var ex = Assert.Throws<WorkflowException>(() => B.SetPinned(ids[3], true, admin));
        Assert.Contains("Unpin one first", ex.Message);
        B.SetPinned(B.Pinned()[0].Id, false, admin);
        B.SetPinned(ids[3], true, admin);
        Assert.Contains(B.Pinned(), p => p.Id == ids[3]);
        Assert.DoesNotContain(B.Page(1).Items, p => p.IsPinned);                               // pinned posts are shown separately, at the top
    }

    [Fact]
    public async Task Removal_by_admin_or_author_hides_the_post_and_its_photos()
    {
        var id = B.Create("Mine", new[] { Upload("p.png", Png(200, 200)) }, _app.Principal("staff"));
        var photo = B.Get(id)!.Photos[0];
        var c = await _app.SignIn("approver1");
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"/board/photo/{photo.Id}?thumb=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await _app.NewClient().GetAsync($"/board/photo/{photo.Id}")).StatusCode);   // sign-in needed
        Assert.Throws<WorkflowException>(() => B.Delete(id, null, _app.Principal("approver1")));                       // not theirs
        B.Delete(id, "Duplicate post", _app.Principal("admin"));
        Assert.True(B.Get(id)!.IsDeleted);
        Assert.DoesNotContain(B.Page(1).Items, p => p.Id == id);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/board/photo/{photo.Id}")).StatusCode);
        Assert.False(File.Exists(B.PhotoPath(photo, false)));
        Assert.Throws<SqliteException>(() => _app.Get<Db>().Exec("DELETE FROM BoardPosts WHERE Id = @id", new { id }));  // the record stays
        var own = B.Create("Typo", Array.Empty<IFormFile>(), _app.Principal("staff"));
        B.Delete(own, null, _app.Principal("staff"));                                                                  // authors may remove their own
        Assert.True(B.Get(own)!.IsDeleted);
        Assert.True(_app.Get<AuditService>().Verify().ok);
    }

    [Fact]
    public async Task Board_page_posting_over_http_and_new_post_badge()
    {
        Assert.Equal(HttpStatusCode.Redirect, (await _app.NewClient().GetAsync("/Board")).StatusCode);
        var a1 = await _app.SignIn("approver1");
        await a1.GetStringAsync("/Board");                                                                  // marks the board as seen
        var staff = await _app.SignIn("staff");
        var token = await TestApp.Token(staff, "/Board");
        using var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { new StringContent("Hello from the field office"), "body" } };
        var f = new ByteArrayContent(Png(300, 200)); f.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(f, "photos", "photo.png");
        var r = await staff.PostAsync("/Board?handler=Create", form);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        var html = await staff.GetStringAsync("/Board");
        Assert.Contains("Hello from the field office", html);
        Assert.Contains("/board/photo/", html);
        Assert.Contains("Hello from the field office", await staff.GetStringAsync("/"));                    // home page card

        await Task.Delay(1100);
        var summary = JsonDocument.Parse(await a1.GetStringAsync("/api/notifications/summary")).RootElement;
        Assert.Equal(1, summary.GetProperty("board").GetInt64());                                           // one new post by someone else
        await a1.GetStringAsync("/Board");
        await Task.Delay(1100);
        summary = JsonDocument.Parse(await a1.GetStringAsync("/api/notifications/summary")).RootElement;
        Assert.Equal(0, summary.GetProperty("board").GetInt64());
        // staff see no pin or remove buttons on other people's posts; administrators do
        Assert.DoesNotContain("handler=Pin", await staff.GetStringAsync("/Board"));
        Assert.Contains("handler=Pin", await (await _app.SignIn("admin")).GetStringAsync("/Board"));
    }

    [Fact]
    public async Task Migration_6_and_backups_include_board_photos()
    {
        Assert.Equal(6, Schema.Version);
        Assert.Empty(_app.Get<Db>().Initialize());
        B.Create("With photo", new[] { Upload("p.png", Png(100, 100)) }, _app.Principal("staff"));
        var backup = _app.Get<BackupService>().Create(_app.Get<AuditService>(), "test");
        using var zip = ZipFile.OpenRead(backup.FullPath);
        Assert.Contains(zip.Entries, e => e.FullName.StartsWith("data/uploads/board/", StringComparison.Ordinal));
        await Task.CompletedTask;
    }
}

public class BoardDisabledTests : IDisposable
{
    private readonly TestApp _app = new(("MessageBoard__Enabled", "false"));
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Switched_off_the_board_is_hidden_and_unavailable()
    {
        var c = await _app.SignIn("staff");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/Board")).StatusCode);
        var home = await c.GetStringAsync("/");
        Assert.DoesNotContain("Message Board", home);
        Assert.DoesNotContain("Message board", home);
    }
}
