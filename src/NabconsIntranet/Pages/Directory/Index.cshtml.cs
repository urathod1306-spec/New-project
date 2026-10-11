using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.StaffDirectory;

public class IndexModel(StaffRepository staff, MasterRepository masters) : AppPage
{
    public string? Q { get; private set; }
    public string? Vertical { get; private set; }
    public string? Location { get; private set; }
    public Paged<StaffMember> Result { get; private set; } = new();
    public List<string> Verticals { get; private set; } = new();
    public List<string> Locations { get; private set; } = new();

    public void OnGet(string? q, string? vertical, string? location, int p = 1)
    {
        Q = Clean(q, 100);
        Vertical = Clean(vertical, 100);
        Location = Clean(location, 100);
        Result = staff.Search(Q, Vertical, Location, Math.Max(1, p));
        Verticals = masters.Names(MasterTypes.Vertical).Union(staff.DistinctValues("Vertical")).OrderBy(x => x).ToList();
        Locations = masters.Names(MasterTypes.Location).Union(staff.DistinctValues("Location")).OrderBy(x => x).ToList();
    }

    public string PageUrl(int page) =>
        $"/Directory?q={Uri.EscapeDataString(Q ?? "")}&vertical={Uri.EscapeDataString(Vertical ?? "")}&location={Uri.EscapeDataString(Location ?? "")}&p={page}";
}
