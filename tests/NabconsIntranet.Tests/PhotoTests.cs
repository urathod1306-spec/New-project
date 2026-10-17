using System.Net;
using System.Net.Http.Headers;
using NabconsIntranet.Infrastructure;
using SkiaSharp;

namespace NabconsIntranet.Tests;

public class PhotoTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private async Task<string> Upload(HttpClient c, byte[] bytes, string fileName, string contentType)
    {
        var token = await TestApp.Token(c, "/Profile", "handler=Photo");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "photo", fileName);
        var r = await c.PostAsync("/Profile?handler=Photo", form);
        Assert.Equal(HttpStatusCode.Redirect, r.StatusCode);
        return await c.GetStringAsync("/Profile");
    }

    [Fact]
    public async Task Valid_photo_is_oriented_cropped_resized_and_stripped_of_EXIF_and_GPS()
    {
        var original = File.ReadAllBytes(Asset("rotated_gps.jpg")); // 600x400, EXIF orientation 6, GPS + camera tags
        Assert.True(IndexOf(original, "Exif"u8.ToArray()) > 0, "test asset should contain EXIF");
        var c = await _app.SignIn("bdu");
        var html = await Upload(c, original, "holiday.jpg", "image/jpeg");
        Assert.Contains("Photo updated", html);

        var u = _app.User("bdu");
        Assert.True(u.PhotoVersion > 0);
        var stored = File.ReadAllBytes(_app.Get<PhotoService>().PhotoPath(u.Id, false));
        Assert.Equal(-1, IndexOf(stored, "Exif"u8.ToArray()));     // no EXIF block at all (so no GPS)
        Assert.Equal(-1, IndexOf(stored, "TestCam"u8.ToArray()));
        using var bmp = SKBitmap.Decode(stored);
        Assert.Equal(400, bmp.Width);
        Assert.Equal(400, bmp.Height);
        // orientation 6 means "rotate 90° clockwise": the red left half must now be on top, blue at the bottom
        var top = bmp.GetPixel(200, 40); var bottom = bmp.GetPixel(200, 360);
        Assert.True(top.Red > 180 && top.Blue < 80, $"top should be red, was {top}");
        Assert.True(bottom.Blue > 180 && bottom.Red < 80, $"bottom should be blue, was {bottom}");
        using var thumb = SKBitmap.Decode(_app.Get<PhotoService>().PhotoPath(u.Id, true));
        Assert.Equal(96, thumb.Width);

        // served only to signed-in users, with the version in the URL
        var photo = await c.GetAsync($"/photo/{u.Id}?v={u.PhotoVersion}");
        Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
        Assert.Equal("image/jpeg", photo.Content.Headers.ContentType!.MediaType);
        Assert.Equal(HttpStatusCode.Redirect, (await _app.NewClient().GetAsync($"/photo/{u.Id}")).StatusCode);
        Assert.False(File.Exists(Path.Combine(_app.Root, "data", "uploads", "photos", "holiday.jpg"))); // never the uploaded name
    }

    [Fact]
    public async Task Oversize_wrong_type_and_disguised_files_are_rejected()
    {
        var c = await _app.SignIn("bdu");
        var big = new byte[2 * 1024 * 1024 + 10];
        new Random(1).NextBytes(big);
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(big, 0);
        Assert.Contains("larger than 2 MB", await Upload(c, big, "big.png", "image/png"));

        var gif = "GIF89a"u8.ToArray().Concat(new byte[2000]).ToArray();
        Assert.Contains("not a JPEG, PNG or WebP", await Upload(c, gif, "photo.gif", "image/gif"));

        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00 }.Concat(new byte[4000]).ToArray(); // "MZ" = Windows program
        Assert.Contains("not a JPEG, PNG or WebP", await Upload(c, exe, "me.jpg", "image/jpeg"));

        Assert.False(_app.Get<PhotoService>().Exists(_app.User("bdu").Id));
    }

    [Fact]
    public async Task Admin_can_remove_a_photo_and_user_falls_back_to_initials()
    {
        var c = await _app.SignIn("bdu");
        await Upload(c, File.ReadAllBytes(Asset("rotated_gps.jpg")), "a.jpg", "image/jpeg");
        var id = _app.User("bdu").Id;
        var admin = await _app.SignIn("admin");
        var token = await TestApp.Token(admin, $"/Admin/UserEdit/{id}", "handler=RemovePhoto");
        await admin.PostAsync($"/Admin/UserEdit/{id}?handler=RemovePhoto", TestApp.Form(token));
        Assert.False(_app.Get<PhotoService>().Exists(id));
        Assert.Equal(0, _app.User("bdu").PhotoVersion);
        Assert.Contains("class=\"avatar lg\">RK<", await c.GetStringAsync("/Profile"));
    }

    [Fact]
    public async Task Contact_card_and_vcard_show_only_directory_fields_and_need_sign_in()
    {
        var staffId = _app.Get<NabconsIntranet.Data.ProfileRepository>().StaffFor(_app.User("bdu"))!.StaffId;
        Assert.Equal(HttpStatusCode.Redirect, (await _app.NewClient().GetAsync($"/directory/card/{staffId}")).StatusCode);
        var c = await _app.SignIn("staff");
        var json = await c.GetStringAsync($"/directory/card/{staffId}");
        Assert.Contains("\"name\":\"Rohan Kapoor\"", json);
        Assert.Contains("\"mobile\":null", json);            // mobile not disclosed in the directory -> not in the card
        Assert.DoesNotContain("birth", json, StringComparison.OrdinalIgnoreCase);
        var vcf = await c.GetStringAsync($"/directory/vcard/{staffId}");
        Assert.StartsWith("BEGIN:VCARD\r\nVERSION:3.0", vcf);
        Assert.Contains("FN:Rohan Kapoor", vcf);
        Assert.DoesNotContain("TEL;TYPE=CELL", vcf);
    }

    private static int IndexOf(byte[] hay, byte[] needle)
    {
        for (int i = 0; i <= hay.Length - needle.Length; i++)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }
}
