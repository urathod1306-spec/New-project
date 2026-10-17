using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.ENotes;

/// <summary>Base for e-Note pages: every page answers "not found" when e-Note is switched off (ENote:Enabled = false).</summary>
public abstract class ENotePage : AppPage
{
    public override void OnPageHandlerExecuting(PageHandlerExecutingContext context)
    {
        if (!HttpContext.RequestServices.GetRequiredService<ENoteOptions>().Enabled) context.Result = NotFound();
        base.OnPageHandlerExecuting(context);
    }

    /// <summary>Reads the list filters from the query string (shared by My e-Notes and the register).</summary>
    protected ENoteFilter ReadFilter(string? tab, string? q, string? state, string? category, string? vertical, string? from, string? to,
        string? initiator, string? holder, int page) => new()
    {
        Tab = tab ?? "pending", Q = Clean(q, 200), State = Clean(state, 40), Category = Clean(category, 100), Vertical = Clean(vertical, 100),
        From = ParseDate(from), To = ParseDate(to), Initiator = Clean(initiator, 100), Holder = Clean(holder, 100), Page = Math.Max(1, page),
    };

    public static string Query(ENoteFilter f, int? page = null, bool includeTab = true)
    {
        var q = new List<string>();
        void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) q.Add(k + "=" + Uri.EscapeDataString(v)); }
        if (includeTab) Add("tab", f.Tab);
        Add("q", f.Q); Add("state", f.State); Add("category", f.Category); Add("vertical", f.Vertical);
        Add("from", f.From?.ToString("yyyy-MM-dd")); Add("to", f.To?.ToString("yyyy-MM-dd")); Add("initiator", f.Initiator); Add("holder", f.Holder);
        if ((page ?? f.Page) > 1) q.Add("page=" + (page ?? f.Page));
        return q.Count > 0 ? "?" + string.Join("&", q) : "";
    }
}
