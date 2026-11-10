using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Inward;

/// <summary>Inward dashboard (DD-08 §6.1). Operators see their own offices only (visibility rule).</summary>
public class IndexModel(InwardRepository repo) : InwardPage
{
    public InwardDashboard D { get; private set; } = new();
    public List<OfficeOption> Offices { get; private set; } = new();
    public long? OfficeId { get; private set; }

    public IActionResult OnGet(long? office)
    {
        if (!Access.SeesRegister(User)) return Redirect("/Inward/Mine");
        var all = repo.Offices(false);
        Offices = Access.IsViewer(User) || Access.IsAuditor(User) || Access.IsAccounts(User) ? all : all.Where(o => Access.IsOperatorOf(User, o.Id)).ToList();
        OfficeId = office > 0 && Offices.Any(o => o.Id == office) ? office : null;
        D = repo.Dashboard(User, OfficeId);
        return Page();
    }
}
