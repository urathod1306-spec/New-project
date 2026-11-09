using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Pages.Compliance;

public sealed record PointTableModel(List<CompliancePoint> Items, bool Lakh, string Empty);

/// <summary>
/// Base for compliance pages (DD-07 §6): every page answers "not found" when the module is switched off, and every
/// page checks access itself through <see cref="ComplianceAccess"/> (404 for anything the user may not see).
/// </summary>
public abstract class CompliancePage : AppPage
{
    protected ComplianceAccess Access => HttpContext.RequestServices.GetRequiredService<ComplianceAccess>();

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (!HttpContext.RequestServices.GetRequiredService<ComplianceOptions>().Enabled) context.Result = NotFound();
        base.OnPageHandlerExecuting(context);
    }

    /// <summary>The same page without the download handler, so a refused download never redirects back to itself.</summary>
    private string BackToPage()
    {
        var q = Request.Query.Where(kv => kv.Key is not ("handler" or "format"))
            .SelectMany(kv => kv.Value.Select(v => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(v ?? ""))).ToList();
        return Request.Path + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    /// <summary>Renders a report into memory (at most two at a time, capped at 50 MB) and returns it as a download.</summary>
    protected async Task<IActionResult> Download(ReportDocument doc, string format)
    {
        using var slot = await ReportGate.EnterAsync(TimeSpan.FromSeconds(20), HttpContext.RequestAborted);
        if (slot == null) { FlashError("The report generator is busy; please try again in a minute."); return Redirect(BackToPage()); }
        var ms = new MemoryStream();
        ReportFormat.Write(doc, format, ms);
        if (ms.Length > 50L * 1024 * 1024) { FlashError("The report is too large; choose fewer events or rows."); return Redirect(BackToPage()); }
        ms.Position = 0;
        return File(ms, ReportFormat.ContentType(format), doc.FileBase + "." + format);
    }

    public static string StatusCss(string s) => s switch
    {
        CStatus.Completed => "success",
        CStatus.PendingExternal => "warning",
        CStatus.ProposedClosure => "info",
        CStatus.Dropped or CStatus.Merged => "neutral",
        _ => "Under",
    };

    public static string StatusLabel(string s) => s switch
    {
        CStatus.PendingExternal => "Guidance awaited",
        CStatus.ProposedClosure => "Proposed for closure",
        _ => s,
    };
}
