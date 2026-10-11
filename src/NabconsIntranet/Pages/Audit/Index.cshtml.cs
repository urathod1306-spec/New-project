using System.Text;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Audit;

public class IndexModel(Db db, AuditService audit, Fmt fmt) : AppPage
{
    public const int PageSize = 50;
    public string? Actor { get; private set; }
    public string? ActionText { get; private set; }
    public DateOnly? From { get; private set; }
    public DateOnly? To { get; private set; }
    public Paged<AuditEntry> Result { get; private set; } = new();
    public List<string> ActionNames { get; private set; } = new();

    private (string where, object args) Build(string? actor, string? action, DateOnly? from, DateOnly? to)
    {
        var w = new List<string>();
        if (!string.IsNullOrWhiteSpace(actor)) w.Add(@"Actor LIKE @actor ESCAPE '\'");
        if (!string.IsNullOrWhiteSpace(action)) w.Add(@"(Action LIKE @action ESCAPE '\' OR Details LIKE @action ESCAPE '\' OR EntityId = @exact)");
        // dates entered in office time; stored in UTC
        var offset = fmt.LocalNow - DateTime.UtcNow;
        string? fromUtc = from?.ToDateTime(TimeOnly.MinValue).Add(-offset).ToString("o");
        string? toUtc = to?.AddDays(1).ToDateTime(TimeOnly.MinValue).Add(-offset).ToString("o");
        if (fromUtc != null) w.Add("Timestamp >= @fromUtc");
        if (toUtc != null) w.Add("Timestamp < @toUtc");
        return (w.Count > 0 ? "WHERE " + string.Join(" AND ", w) : "",
            new { actor = SearchText.Like(actor), action = SearchText.Like(action), exact = action?.Trim(), fromUtc, toUtc });
    }

    public void OnGet(string? actor, string? action, DateOnly? from, DateOnly? to, int p = 1)
    {
        Actor = Clean(actor, 100);
        ActionText = Clean(action, 100);
        From = from;
        To = to;
        var (where, args) = Build(Actor, ActionText, From, To);
        p = Math.Max(1, p);
        Result = new Paged<AuditEntry>
        {
            Total = db.Scalar<long>("SELECT COUNT(*) FROM AuditLog " + where, args),
            Items = db.Query<AuditEntry>($"SELECT * FROM AuditLog {where} ORDER BY Id DESC LIMIT {PageSize} OFFSET {(p - 1) * PageSize}", args),
            Page = p,
            PageSize = PageSize,
        };
        ActionNames = db.Strings("SELECT DISTINCT Action FROM AuditLog ORDER BY Action");
    }

    public IActionResult OnGetExport(string? actor, string? action, DateOnly? from, DateOnly? to)
    {
        var (where, args) = Build(Clean(actor, 100), Clean(action, 100), from, to);
        var sw = new StringWriter();
        audit.ExportCsv(sw, where, args);
        audit.Log("Audit.Exported", "AuditLog", null, $"filters: actor={actor}; action={action}; from={from}; to={to}");
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sw.ToString())).ToArray();
        return File(bytes, "text/csv", $"audit-log-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    public IActionResult OnPostVerify()
    {
        var (ok, count, broken) = audit.Verify();
        audit.Log(ok ? "Audit.Verified" : "Audit.VerifyFailed", "AuditLog", broken, ok ? $"{count} entries verified" : $"chain broken at entry {broken}");
        if (ok) Flash($"Audit log integrity verified: all {count} entries are intact and in sequence.");
        else FlashError($"AUDIT LOG INTEGRITY FAILURE at entry #{broken} (after {count} valid entries). The database may have been altered outside the application. Inform IT security.");
        return RedirectToPage();
    }

    public string PageUrl(int page) =>
        $"/Audit?actor={Uri.EscapeDataString(Actor ?? "")}&action={Uri.EscapeDataString(ActionText ?? "")}&from={From:yyyy-MM-dd}&to={To:yyyy-MM-dd}&p={page}";
}
