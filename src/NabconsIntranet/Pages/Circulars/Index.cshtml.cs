using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Circulars;

public class IndexModel(CircularRepository circulars, MasterRepository masters) : AppPage
{
    public CircularFilter Filter { get; private set; } = new();
    public Paged<Circular> Result { get; private set; } = new();
    public List<string> Categories { get; private set; } = new();
    public List<string> Verticals { get; private set; } = new();
    public List<int> Years { get; private set; } = new();

    public void OnGet(string? q, string? category, string? vertical, int? year, string? status, bool withdrawn = false, int p = 1)
    {
        Filter = new CircularFilter
        {
            Q = Clean(q, 200), Category = Clean(category, 100), Vertical = Clean(vertical, 100), Year = year,
            Status = User.SeesAllCirculars() ? Clean(status, 20) : null, IncludeWithdrawn = withdrawn, Page = Math.Max(1, p),
        };
        Result = circulars.Search(Filter, User);
        Categories = masters.Names(MasterTypes.CircularCategory);
        Verticals = masters.Names(MasterTypes.Vertical);
        Years = circulars.Years();
    }

    public string PageUrl(int page) =>
        $"/Circulars?q={Uri.EscapeDataString(Filter.Q ?? "")}&category={Uri.EscapeDataString(Filter.Category ?? "")}&vertical={Uri.EscapeDataString(Filter.Vertical ?? "")}" +
        $"&year={Filter.Year}&status={Uri.EscapeDataString(Filter.Status ?? "")}&withdrawn={Filter.IncludeWithdrawn.ToString().ToLowerInvariant()}&p={page}";
}
