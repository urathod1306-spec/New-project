using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Leads;

public class MineModel(LeadRepository leads) : AppPage
{
    public List<Lead> Items { get; private set; } = new();
    public void OnGet() => Items = leads.Mine(User.UserId());
}
