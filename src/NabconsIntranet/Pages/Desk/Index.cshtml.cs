using NabconsIntranet.Data;

namespace NabconsIntranet.Pages.Desk;

public class IndexModel(DeskService desk) : AppPage
{
    public DeskView View { get; private set; } = new() { Counts = new DeskCounts(0, 0, 0, 0, 0, 0, 0, 0, 0) };

    public void OnGet() => View = desk.View(User);
}
