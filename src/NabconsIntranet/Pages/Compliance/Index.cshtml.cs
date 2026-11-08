using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>Compliance dashboard (DD-07 §6): tiles, source × status matrix, ageing, by vertical and officer, monthly closures.</summary>
public class IndexModel(ComplianceRepository repo) : CompliancePage
{
    public ComplianceDashboard D { get; private set; } = new();
    public bool IsAdmin { get; private set; }

    public IActionResult OnGet()
    {
        if (!Access.SeesAll(User)) return Redirect("/Compliance/Mine");
        IsAdmin = Access.IsAdmin(User);
        D = repo.Dashboard(User);
        return Page();
    }

    public string StatusLabelShort(string s) => s switch { CStatus.PendingExternal => "Guidance awaited", CStatus.ProposedClosure => "Proposed closure", _ => s };

    public long Cell(string source, string status) => D.Matrix.Where(m => m.Key == source && m.Key2 == status).Sum(m => m.N);
}
