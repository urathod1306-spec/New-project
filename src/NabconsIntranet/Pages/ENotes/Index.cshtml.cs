using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.ENotes;

public class IndexModel(ENoteRepository notes, MasterRepository masters) : ENotePage
{
    public ENoteFilter Filter { get; private set; } = new();
    public Paged<ENoteRow> Result { get; private set; } = new();
    public List<string> Categories { get; private set; } = new();
    public List<string> Verticals { get; private set; } = new();

    public void OnGet(string? tab, string? q, string? state, string? category, string? vertical, string? from, string? to, string? initiator, int page = 1)
    {
        Filter = ReadFilter(tab, q, state, category, vertical, from, to, initiator, null, page);
        if (!ENoteRepository.Tabs.ContainsKey(Filter.Tab) || (Filter.Tab == "approved" && !notes.Options.AllStaffSeeApproved)) Filter.Tab = "pending";
        Result = notes.List(User, Filter);
        Categories = masters.Names(MasterTypes.ENoteCategory);
        Verticals = masters.Names(MasterTypes.Vertical);
    }

    public string PageUrl(int page) => "/ENotes" + Query(Filter, page);
}
