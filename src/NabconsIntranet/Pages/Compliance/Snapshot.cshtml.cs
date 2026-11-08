using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>A final ATR: its content, the integrity check, and downloads regenerated only from the stored rows.</summary>
public class SnapshotModel(AtrBuilder atr, AuditService audit) : CompliancePage
{
    public ComplianceSnapshot S { get; private set; } = new();
    public AtrHeader Header { get; private set; } = new();
    public List<AtrItem> Items { get; private set; } = new();
    public bool Verified { get; private set; }
    public bool IsAdmin { get; private set; }

    private IActionResult? Load(long id)
    {
        var s = atr.Snapshot(id);
        if (s == null || !atr.CanView(s, User)) return NotFound();
        S = s;
        Header = System.Text.Json.JsonSerializer.Deserialize<AtrHeader>(s.HeaderJson) ?? new();
        Items = atr.SnapshotItems(id);
        Verified = atr.Verify(s);
        IsAdmin = Access.IsAdmin(User);
        return null;
    }

    public IActionResult OnGet(long id) => Load(id) ?? Page();

    public async Task<IActionResult> OnGetDownloadAsync(long id, string format)
    {
        if (Load(id) is { } r) return r;
        if (!ReportFormat.All.Contains(format)) return NotFound();
        audit.Log("Compliance.AtrDownloaded", "ComplianceSnapshot", id, $"snapshot #{id}; {format}; verified={Verified}");
        return await Download(atr.Regenerate(S), format);
    }

    public IActionResult OnPostVoid(long id, string? reason)
    {
        if (Load(id) is { } r) return r;
        try { atr.Void(id, reason, User); Flash("Snapshot marked void. It stays on record and is no longer used as the previous ATR."); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return Redirect($"/Compliance/Snapshot/{id}");
    }
}
