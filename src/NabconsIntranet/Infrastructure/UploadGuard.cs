using Microsoft.AspNetCore.Http.Features;

namespace NabconsIntranet.Infrastructure;

/// <summary>
/// Register-only modules (Compliance; the Inward Register in a later release) never accept files (DD-07 §3.5).
/// For POST/PUT/PATCH to their routes this middleware caps the request body (256 KB; 2 MB on the import page) and
/// accepts only urlencoded forms — multipart is allowed on exactly one page, the in-memory CSV import.
/// </summary>
public static class UploadGuard
{
    public static readonly string[] NoUploadPrefixes = { "/Compliance", "/Inward" };
    public static readonly string[] MultipartAllowed = { "/Compliance/Import", "/Inward/ProjectCodes", "/Inward/Payments" };
    public const long BodyLimit = 256 * 1024, ImportBodyLimit = 2 * 1024 * 1024;

    private static bool IsWrite(HttpRequest r) => HttpMethods.IsPost(r.Method) || HttpMethods.IsPut(r.Method) || HttpMethods.IsPatch(r.Method) || HttpMethods.IsDelete(r.Method);

    public static bool Applies(HttpRequest r) => IsWrite(r) && NoUploadPrefixes.Any(p => r.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));

    public static bool IsImport(HttpRequest r) => MultipartAllowed.Any(p => r.Path.Equals(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>415 unless the media type (ignoring "; charset=" and other parameters) is allowed for this path.</summary>
    public static bool Refuses(HttpRequest r)
    {
        if (!Applies(r)) return false;
        var media = (r.ContentType ?? "").Split(';')[0].Trim();
        if (media.Equals("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)) return false;
        if (media.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase) && IsImport(r)) return false;
        return !(media.Length == 0 && (r.ContentLength ?? 0) == 0);      // an empty POST is harmless
    }

    public static IApplicationBuilder UseUploadGuard(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var r = ctx.Request;
        if (Applies(r))
        {
            var limit = IsImport(r) ? ImportBodyLimit : BodyLimit;
            if (r.ContentLength > limit)
            {
                ctx.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                await ctx.Response.WriteAsync("The request is too large.");
                return;
            }
            var feature = ctx.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature is { IsReadOnly: false }) feature.MaxRequestBodySize = limit;
            if (Refuses(r))
            {
                ctx.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await ctx.Response.WriteAsync("Files cannot be uploaded here: this register keeps details only.");
                return;
            }
        }
        await next();
    });
}
