using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Admin;

public class MastersModel(MasterRepository masters, AuditService audit) : AppPage
{
    public string Type { get; private set; } = MasterTypes.Vertical;
    public List<MasterItem> Items { get; private set; } = new();

    private static string Valid(string? type) => type != null && MasterTypes.Labels.ContainsKey(type) ? type : MasterTypes.Vertical;

    public void OnGet(string? type)
    {
        Type = Valid(type);
        Items = masters.List(Type, activeOnly: false);
    }

    public IActionResult OnPostAdd(string? type, string? name)
    {
        Type = Valid(type);
        name = Clean(name, 100);
        if (name == null) FlashError("Enter a name.");
        else if (masters.Exists(Type, name)) FlashError($"“{name}” already exists.");
        else
        {
            var id = masters.Add(Type, name, masters.List(Type, false).Count);
            audit.Log("Master.Added", "MasterItem", id, $"{Type}: {name}");
            Flash($"“{name}” added.");
        }
        return RedirectToPage(new { type = Type });
    }

    /// <summary>Verticals only: the short code used in e-Note numbers. Changing it affects only notes numbered afterwards.</summary>
    public IActionResult OnPostCode(long id, string? code)
    {
        var m = masters.Get(id);
        if (m == null || m.Type != MasterTypes.Vertical) return NotFound();
        var clean = ENoteMaster.CleanCode(code);
        if (!string.IsNullOrWhiteSpace(code) && clean == null) { FlashError("A code is 2 to 8 letters or digits, e.g. RID or HR."); return RedirectToPage(new { type = m.Type }); }
        if (clean != null && masters.List(MasterTypes.Vertical, false).Any(x => x.Id != id && string.Equals(x.Code, clean, StringComparison.OrdinalIgnoreCase)))
        { FlashError($"The code {clean} is already used by another vertical."); return RedirectToPage(new { type = m.Type }); }
        masters.SetCode(id, clean);
        audit.Log("Master.CodeChanged", "MasterItem", id, $"{m.Name}: '{m.Code}' -> '{clean}'");
        Flash(clean == null ? $"Code removed for “{m.Name}” (new e-Notes will use GEN)." : $"“{m.Name}” will use {clean} in new e-Note numbers.");
        return RedirectToPage(new { type = m.Type });
    }

    public IActionResult OnPostToggle(long id)
    {
        var m = masters.Get(id);
        if (m == null) return NotFound();
        masters.SetActive(id, !m.IsActive);
        audit.Log(m.IsActive ? "Master.Deactivated" : "Master.Reactivated", "MasterItem", id, $"{m.Type}: {m.Name}");
        Flash($"“{m.Name}” {(m.IsActive ? "hidden from selection lists" : "re-activated")}.");
        return RedirectToPage(new { type = m.Type });
    }
}
