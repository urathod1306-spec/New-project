using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Pages.Inward;

/// <summary>A row list shown by the _ItemTable partial.</summary>
public sealed record ItemTableModel(List<InwardItem> Items, string Empty, bool Invoices = false);

/// <summary>
/// Base for inward pages (DD-08 §6): "not found" when the module is switched off; every page checks access itself
/// through <see cref="InwardAccess"/> (404 for anything the user may not see).
/// </summary>
public abstract class InwardPage : AppPage
{
    protected InwardAccess Access => HttpContext.RequestServices.GetRequiredService<InwardAccess>();

    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (!HttpContext.RequestServices.GetRequiredService<InwardOptions>().Enabled) context.Result = NotFound();
        base.OnPageHandlerExecuting(context);
    }

    private string BackToPage()
    {
        var q = Request.Query.Where(kv => kv.Key is not ("handler" or "format"))
            .SelectMany(kv => kv.Value.Select(v => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(v ?? ""))).ToList();
        return Request.Path + (q.Count > 0 ? "?" + string.Join("&", q) : "");
    }

    /// <summary>Renders a report into memory (two at a time, 50 MB cap) and returns it as a download.</summary>
    protected async Task<IActionResult> Download(ReportDocument doc, string format)
    {
        using var slot = await ReportGate.EnterAsync(TimeSpan.FromSeconds(20), HttpContext.RequestAborted);
        if (slot == null) { FlashError("The report generator is busy; please try again in a minute."); return Redirect(BackToPage()); }
        var ms = new MemoryStream();
        ReportFormat.Write(doc, format, ms);
        if (ms.Length > 50L * 1024 * 1024) { FlashError("The report is too large; narrow the filter."); return Redirect(BackToPage()); }
        ms.Position = 0;
        return File(ms, ReportFormat.ContentType(format), doc.FileBase + "." + format);
    }

    public static string StatusCss(InwardItem i, DateOnly today) =>
        i.InvoiceStatus is { } s ? s switch
        {
            InvStatus.Paid => "success", InvStatus.OnHold => "warning", InvStatus.PartPaid or InvStatus.SentToAccounts => "info",
            InvStatus.Rejected or InvStatus.ReturnedToVendor or InvStatus.Cancelled or InvStatus.ClosedShort => "neutral", _ => "Under",
        }
        : i.Status switch { ItemStatus.Closed => i.ClosureType == Closure.Cancelled ? "neutral" : "success", ItemStatus.Unassigned => "warning", _ => i.IsOverdue(today) ? "danger" : "Under" };

    public static string StatusText(InwardItem i) =>
        i.InvoiceStatus is { } s ? InvStatus.Label(s) : i.Status == ItemStatus.Closed ? Closure.Label(i.ClosureType) : ItemStatus.Label(i.Status);
}

/// <summary>What the _InwardForm partial needs (DD-08 §6.3).</summary>
public sealed class InwardFormModel
{
    public InwardInput Input { get; init; } = new();
    public bool Edit { get; init; }
    public bool SelfInward { get; init; }
    public bool MetadataOnly { get; init; }
    public bool ShowInvoice { get; init; } = true;
    public bool AmountsOnly { get; init; }
    public bool Blocked { get; init; }
    public List<OfficeOption> Offices { get; init; } = new();
    public List<PersonOption> Users { get; init; } = new();
    public List<string> Categories { get; init; } = new();
    public List<ProjectCode> ProjectCodes { get; init; } = new();
    public int MaxBackdateDays { get; init; }
    public int DefaultTerms { get; init; }
    public string ReplyDaysHint { get; init; } = "";
}
