using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>The officer's own action points (primary or supporting): open first, then those closed in the last 90 days.</summary>
public class MineModel(ComplianceRepository repo) : CompliancePage
{
    public List<CompliancePoint> Items { get; private set; } = new();
    public void OnGet() => Items = repo.Mine(User.UserId());
}
