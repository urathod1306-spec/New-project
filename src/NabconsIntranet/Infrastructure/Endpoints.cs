using System.IO.Compression;
using System.Text;
using NabconsIntranet.Data;

namespace NabconsIntranet.Infrastructure;

/// <summary>Small authenticated endpoints: staff photos, directory contact cards (JSON) and vCards.</summary>
public static class Endpoints
{
    public const string SummaryPath = "/api/notifications/summary";

    public static bool IsPoll(HttpRequest r) => r.Path.Equals(SummaryPath, StringComparison.OrdinalIgnoreCase);
    public static bool IsApi(HttpRequest r) => r.Path.StartsWithSegments("/api");

    public static void MapIntranetEndpoints(this WebApplication app)
    {
        // Badge poll: tiny JSON, answered from memory when nothing has changed (ETag / 304).
        app.MapGet(SummaryPath, (HttpContext ctx, DeskService desk) =>
        {
            var c = desk.Counts(ctx.User);
            var etag = "\"" + c.Token + "\"";
            ctx.Response.Headers.CacheControl = "private, no-cache";
            ctx.Response.Headers.Remove("Pragma");
            ctx.Response.Headers.ETag = etag;
            if (ctx.Request.Headers.IfNoneMatch.ToString() == etag) return Results.StatusCode(StatusCodes.Status304NotModified);
            return Results.Json(new
            {
                unread = c.Unread, desk = c.Total, inbox = c.Inbox, admin = c.AdminPending, leads = c.NewLeads,
                enotes = c.ENotesPending, token = c.Token,
            });
        }).RequireAuthorization();

        // Photos are served only to signed-in users, from data\uploads\photos (never from wwwroot).
        app.MapGet("/photo/{userId:long}", (long userId, bool? thumb, long? v, HttpContext ctx, PhotoService photos) =>
        {
            var path = photos.PhotoPath(userId, thumb == true);
            if (!File.Exists(path)) return Results.NotFound();
            // The ?v= version changes on every replacement, so a long private cache is safe and replacements show at once.
            ctx.Response.Headers.CacheControl = v is > 0 ? "private, max-age=31536000, immutable" : "private, no-cache";
            ctx.Response.Headers.Remove("Pragma");
            return Results.File(path, "image/jpeg");
        }).RequireAuthorization();

        // ---- e-Note: attachment download, all attachments as one streamed ZIP, and the "Mark to" type-ahead ----
        app.MapGet("/ENotes/Attachment/{id:long}", (long id, HttpContext ctx, ENoteRepository notes, ENoteOptions eo, FileStore files, AuditService audit) =>
        {
            if (!eo.Enabled) return Results.NotFound();
            var a = notes.Attachment(id);
            var n = a != null ? notes.Get(a.NoteId) : null;
            if (a == null || n == null || !notes.CanView(n, ctx.User)) return Results.NotFound();
            var s = files.OpenRead(a.StoredPath);
            if (s == null) return Results.NotFound();
            audit.Log("ENote.Downloaded", "ENote", n.Id, $"{n.DisplayNumber}: {a.OriginalFileName}");
            return Results.File(s, a.ContentType, a.OriginalFileName);           // sent as an attachment, with nosniff
        }).RequireAuthorization();

        app.MapGet("/ENotes/{id:long}/Attachments.zip", async (long id, HttpContext ctx, ENoteRepository notes, ENoteOptions eo, FileStore files, AuditService audit) =>
        {
            if (!eo.Enabled) return Results.NotFound();
            var n = notes.Get(id);
            if (n == null || !notes.CanView(n, ctx.User)) return Results.NotFound();
            var list = notes.Attachments(id);
            if (list.Count == 0) return Results.NotFound();
            audit.Log("ENote.Downloaded", "ENote", n.Id, $"{n.DisplayNumber}: all attachments ({list.Count} files, ZIP)");
            var name = new string((n.NoteNumber ?? "draft-" + n.Id).Select(ch => char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_').ToArray());
            ctx.Response.ContentType = "application/zip";
            ctx.Response.Headers.ContentDisposition = $"attachment; filename=\"{name}-attachments.zip\"";
            // Streamed file by file straight to the browser: nothing is built in memory or written to a temp folder.
            var output = new WriteOnlyAsyncStream(ctx.Response.Body);
            await using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var k = 0;
                foreach (var a in list)
                {
                    await using var src = files.OpenRead(a.StoredPath);
                    if (src == null) continue;
                    var entryName = $"{++k:D2} {a.OriginalFileName}" + (a.SupersededById != null ? " (superseded)" : "");
                    entryName = entryName.Replace('/', '_').Replace('\\', '_');
                    if (!used.Add(entryName)) entryName = $"{a.Id}-{entryName}";
                    var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                    await using var dst = await entry.OpenAsync();
                    await src.CopyToAsync(dst, ctx.RequestAborted);
                }
            }
            await output.DisposeAsync();      // sends the last buffered block (the ZIP's central directory)
            return Results.Empty;
        }).RequireAuthorization();

        app.MapGet("/api/users/lookup", (string? q, HttpContext ctx, ENoteRepository notes) =>
            Results.Json(notes.Lookup(q, ctx.User.UserId()).Select(u => new { id = u.Id, name = u.Name, designation = u.Designation, vertical = u.Vertical })))
            .RequireAuthorization();

        app.MapGet("/directory/card/{staffId:long}", (long staffId, StaffRepository staff) =>
        {
            var s = staff.GetForCard(staffId);
            if (s == null) return Results.NotFound();
            return Results.Json(new
            {
                id = s.StaffId,
                name = s.FullName,
                initials = Fmt.Initials(s.FullName),
                designation = s.Designation,
                vertical = s.Vertical,
                location = s.Location,
                extension = s.Extension,
                mobile = s.ShowMobile ? s.Mobile : null,     // only what the directory itself shows
                email = s.OfficialEmail,
                photo = s.PhotoUserId != null ? $"/photo/{s.PhotoUserId}?v={s.PhotoVersion}" : null,
                vcard = $"/directory/vcard/{s.StaffId}",
            });
        }).RequireAuthorization();

        app.MapGet("/directory/vcard/{staffId:long}", (long staffId, StaffRepository staff, IntranetOptions o, AuditService audit) =>
        {
            var s = staff.GetForCard(staffId);
            if (s == null) return Results.NotFound();
            static string E(string? v) => (v ?? "").Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");
            var parts = s.FullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var last = parts.Length > 1 ? parts[^1] : "";
            var first = parts.Length > 1 ? string.Join(' ', parts[..^1]) : s.FullName;
            var sb = new StringBuilder();
            sb.Append("BEGIN:VCARD\r\nVERSION:3.0\r\n");
            sb.Append($"N:{E(last)};{E(first)};;;\r\nFN:{E(s.FullName)}\r\n");
            sb.Append($"ORG:{E(o.OrganisationName)}{(string.IsNullOrEmpty(s.Vertical) ? "" : ";" + E(s.Vertical))}\r\n");
            if (!string.IsNullOrEmpty(s.Designation)) sb.Append($"TITLE:{E(s.Designation)}\r\n");
            if (s.ShowMobile && !string.IsNullOrEmpty(s.Mobile)) sb.Append($"TEL;TYPE=CELL:{E(s.Mobile)}\r\n");
            if (!string.IsNullOrEmpty(s.Extension)) sb.Append($"TEL;TYPE=WORK,VOICE:{E(s.Extension)}\r\n");
            if (!string.IsNullOrEmpty(s.OfficialEmail)) sb.Append($"EMAIL;TYPE=INTERNET,WORK:{E(s.OfficialEmail)}\r\n");
            if (!string.IsNullOrEmpty(s.Location)) sb.Append($"ADR;TYPE=WORK:;;{E(s.Location)};;;;\r\n");
            sb.Append("END:VCARD\r\n");
            var file = new string(s.FullName.Where(ch => char.IsLetterOrDigit(ch) || ch == ' ').ToArray()).Trim().Replace(' ', '_');
            return Results.File(Encoding.UTF8.GetBytes(sb.ToString()), "text/vcard", (file.Length == 0 ? "contact" : file) + ".vcf");
        }).RequireAuthorization();
    }
}

/// <summary>
/// Lets ZipArchive write to the response body: ZipArchive writes synchronously, Kestrel only accepts asynchronous writes,
/// so small writes are buffered and flushed asynchronously in 64 KB blocks.
/// </summary>
internal sealed class WriteOnlyAsyncStream(Stream inner) : Stream
{
    private readonly MemoryStream _buffer = new();
    private long _position;
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _position;
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override async Task FlushAsync(CancellationToken ct) { await Drain(ct); await inner.FlushAsync(ct); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count)
    {
        _buffer.Write(buffer, offset, count);
        _position += count;
        if (_buffer.Length >= 64 * 1024) Drain(CancellationToken.None).GetAwaiter().GetResult();
    }
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
    {
        _buffer.Write(buffer.Span);
        _position += buffer.Length;
        if (_buffer.Length >= 64 * 1024) await Drain(ct);
    }
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) => WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();
    private async Task Drain(CancellationToken ct)
    {
        if (_buffer.Length == 0) return;
        await inner.WriteAsync(_buffer.GetBuffer().AsMemory(0, (int)_buffer.Length), ct);
        _buffer.SetLength(0);
    }
    public override async ValueTask DisposeAsync() { await Drain(CancellationToken.None); await inner.FlushAsync(); await base.DisposeAsync(); }
}
