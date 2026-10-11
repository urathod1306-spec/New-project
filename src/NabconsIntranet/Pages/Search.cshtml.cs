using NabconsIntranet.Data;

namespace NabconsIntranet.Pages;

public class SearchModel(StaffRepository staff, CircularRepository circulars) : AppPage
{
    public string? Q { get; private set; }
    public Paged<StaffMember> People { get; private set; } = new();
    public Paged<Circular> Documents { get; private set; } = new();

    public void OnGet(string? q)
    {
        Q = Clean(q, 100);
        if (Q == null) return;
        People = staff.Search(Q, null, null, 1);
        Documents = circulars.Search(new CircularFilter { Q = Q, Page = 1 }, User);
    }
}
