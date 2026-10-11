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
