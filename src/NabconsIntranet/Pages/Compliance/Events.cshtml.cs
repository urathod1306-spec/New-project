using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Compliance;

public class EventsModel(ComplianceRepository repo, MasterRepository masters) : CompliancePage
{
    public List<ComplianceEvent> Items { get; private set; } = new();
    public List<MasterItem> Forums { get; private set; } = new();
    public string? Source { get; private set; }
    public long? Forum { get; private set; }
    public string? Year { get; private set; }
    public bool IsAdmin { get; private set; }

    public IActionResult OnGet(string? source, long? forum, string? year)
    {
        if (!Access.SeesAll(User)) return NotFound();
        (Source, Forum, Year) = (source, forum, year);
        Items = repo.Events(User, source, forum, year);
        Forums = masters.List(MasterTypes.ComplianceForum, false);
        IsAdmin = Access.IsAdmin(User);
        return Page();
    }
}
