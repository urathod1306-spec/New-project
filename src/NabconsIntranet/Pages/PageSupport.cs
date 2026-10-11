using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NabconsIntranet.Pages;

public sealed record PagerModel(int Page, int Pages, Func<int, string> Url);

public abstract class AppPage : PageModel
{
    protected void Flash(string message) => TempData["Message"] = message;
    protected void FlashError(string message) => TempData["Error"] = message;

    protected static string? Clean(string? s, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        return s.Length > max ? s[..max] : s;
    }

    protected static string? IsoDate(DateOnly? d) => d?.ToString("yyyy-MM-dd");

    protected static DateOnly? ParseDate(string? iso) =>
        DateOnly.TryParseExact(iso ?? "", "yyyy-MM-dd", out var d) ? d : null;
}

public sealed record RequestTableModel(List<NabconsIntranet.Data.ApprovalRequest> Items, bool ShowSubmitter, string Empty);
