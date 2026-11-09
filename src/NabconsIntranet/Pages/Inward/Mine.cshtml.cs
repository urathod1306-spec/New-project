using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Inward;

/// <summary>My items (DD-08 §6.4): with me, copied to me, inwarded by me, closed in the last 90 days.</summary>
public class MineModel(InwardRepository repo) : InwardPage
{
    public static readonly Dictionary<string, string> Tabs = new() { ["with"] = "With me", ["copied"] = "Copied to me", ["inwarded"] = "Inwarded by me", ["closed"] = "Closed (90 days)" };
    public string Tab { get; private set; } = "with";
    public List<InwardItem> Items { get; private set; } = new();

    public IActionResult OnGet(string? tab)
    {
        Tab = tab != null && Tabs.ContainsKey(tab) ? tab : "with";
        Items = repo.Mine(User, Tab);
        return Page();
    }
}
