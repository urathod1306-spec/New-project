using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Compliance;

public class SnapshotsModel(AtrBuilder atr) : CompliancePage
{
    public List<ComplianceSnapshot> Items { get; private set; } = new();

    public IActionResult OnGet()
    {
        if (!Access.SeesAll(User)) return NotFound();
        Items = atr.Snapshots(User);
        return Page();
    }
}
