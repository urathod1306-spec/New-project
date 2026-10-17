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
