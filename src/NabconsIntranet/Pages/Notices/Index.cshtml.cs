using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Notices;

public class IndexModel(NoticeRepository notices, AuditService audit, Fmt fmt) : AppPage
{
    public sealed class InputModel
    {
        [Required, StringLength(150)] public string Title { get; set; } = "";
        [StringLength(2000)] public string? Body { get; set; }
        [Display(Name = "Mark as important")] public bool IsImportant { get; set; }
        [Required, Display(Name = "Show from")] public DateOnly? StartsOn { get; set; }
        [Display(Name = "Show until (optional)")] public DateOnly? EndsOn { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public List<Notice> Items { get; private set; } = new();

    public void OnGet()
    {
        Input.StartsOn = fmt.Today;
        Items = notices.All();
    }

    public IActionResult OnPost()
    {
        if (Input.EndsOn != null && Input.StartsOn != null && Input.EndsOn < Input.StartsOn)
            ModelState.AddModelError("Input.EndsOn", "The end date must be on or after the start date.");
        if (!ModelState.IsValid) { Items = notices.All(); return Page(); }
        var id = notices.Add(new Notice
        {
            Title = Input.Title.Trim(), Body = Clean(Input.Body, 2000), IsImportant = Input.IsImportant,
            StartsOn = IsoDate(Input.StartsOn)!, EndsOn = IsoDate(Input.EndsOn), PostedBy = User.DisplayName(), PostedAt = Db.Now(),
        });
        audit.Log("Notice.Posted", "Notice", id, Input.Title);
        Flash("Notice posted.");
        return RedirectToPage();
    }

    public IActionResult OnPostDeactivate(long id)
    {
        notices.Deactivate(id);
        audit.Log("Notice.Removed", "Notice", id);
        Flash("Notice removed from the notice board.");
        return RedirectToPage();
    }
}
