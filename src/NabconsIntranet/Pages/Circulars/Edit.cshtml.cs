using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Circulars;

public class EditModel(CircularRepository circulars, MasterRepository masters, FileStore files, AuditService audit, Fmt fmt) : AppPage
{
    public sealed class InputModel
    {
        [Required, StringLength(60), Display(Name = "Circular / order number")] public string CircularNumber { get; set; } = "";
        [Required, StringLength(250)] public string Title { get; set; } = "";
        [Required] public string Category { get; set; } = "";
        [Display(Name = "Issuing vertical")] public string? IssuingVertical { get; set; }
        [Required, Display(Name = "Issue date")] public DateOnly? IssueDate { get; set; }
        [Display(Name = "Effective date")] public DateOnly? EffectiveDate { get; set; }
        [StringLength(500)] public string? Keywords { get; set; }
        [StringLength(4000)] public string? Synopsis { get; set; }
        [Required] public string Visibility { get; set; } = Data.Visibility.AllStaff;
        [Display(Name = "Supersedes")] public long? SupersedesId { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? Upload { get; set; }
    public long Id { get; private set; }
    public List<string> Categories { get; private set; } = new();
    public List<string> Verticals { get; private set; } = new();
    public List<Circular> SupersedeOptions { get; private set; } = new();
    public string Accept => files.AcceptAttribute;
    public string AllowedList => files.AllowedList;

    private void LoadLists()
    {
        Categories = masters.Names(MasterTypes.CircularCategory);
        Verticals = masters.Names(MasterTypes.Vertical);
        SupersedeOptions = circulars.PublishedForPicker(Id);
    }

    public IActionResult OnGet(long? id)
    {
        Id = id ?? 0;
        if (id != null)
        {
            var c = circulars.Get(id.Value);
            if (c == null) return NotFound();
            if (c.Status == CircularStatus.Withdrawn) { FlashError("Withdrawn circulars cannot be edited."); return Redirect($"/Circulars/Details/{c.Id}"); }
            Input = new InputModel
            {
                CircularNumber = c.CircularNumber, Title = c.Title, Category = c.Category, IssuingVertical = c.IssuingVertical,
                IssueDate = ParseDate(c.IssueDate), EffectiveDate = ParseDate(c.EffectiveDate), Keywords = c.Keywords,
                Synopsis = c.Synopsis, Visibility = c.Visibility, SupersedesId = c.SupersedesId,
            };
        }
        else
        {
            Input.IssueDate = fmt.Today;
            Input.IssuingVertical = User.Vertical();
        }
        LoadLists();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long? id, bool publish = false)
    {
        Id = id ?? 0;
        LoadLists();
        Circular? existing = null;
        if (id != null)
        {
            existing = circulars.Get(id.Value);
            if (existing == null) return NotFound();
            if (existing.Status == CircularStatus.Withdrawn) return Forbid();
        }
        Input.CircularNumber = (Input.CircularNumber ?? "").Trim();
        if (!Data.Visibility.Labels.ContainsKey(Input.Visibility)) ModelState.AddModelError("Input.Visibility", "Choose a valid visibility.");
        if (!Categories.Contains(Input.Category) && existing?.Category != Input.Category) ModelState.AddModelError("Input.Category", "Choose a category from the list.");
        if (circulars.NumberExists(Input.CircularNumber, Id))
            ModelState.AddModelError("Input.CircularNumber", "A circular with this number already exists. Circular numbers must be unique.");
        if (Input.Visibility == Data.Visibility.Vertical && string.IsNullOrEmpty(Input.IssuingVertical))
            ModelState.AddModelError("Input.IssuingVertical", "Select the issuing vertical for vertical-only circulars.");
        if (existing == null && Upload == null) ModelState.AddModelError(nameof(Upload), "Attach the circular file.");
        if (Input.SupersedesId != null && SupersedeOptions.All(s => s.Id != Input.SupersedesId) && existing?.SupersedesId != Input.SupersedesId)
            ModelState.AddModelError("Input.SupersedesId", "Choose a published circular to supersede.");
        if (!ModelState.IsValid) return Page();

        StoredFile? stored = null;
        if (Upload != null)
        {
            try { stored = await files.SaveAsync(Upload, "circulars"); }
            catch (UploadRejectedException ex)
            {
                audit.Log("Upload.Rejected", "Circular", id, ex.Message);
                ModelState.AddModelError(nameof(Upload), ex.Message);
                return Page();
            }
        }

        var c = existing ?? new Circular { CreatedBy = User.UserName() };
        c.CircularNumber = Input.CircularNumber;
        c.Title = Input.Title.Trim();
        c.Category = Input.Category;
        c.IssuingVertical = Clean(Input.IssuingVertical, 100);
        c.IssueDate = IsoDate(Input.IssueDate)!;
        c.EffectiveDate = IsoDate(Input.EffectiveDate);
        c.Keywords = Clean(Input.Keywords, 500);
        c.Synopsis = Clean(Input.Synopsis, 4000);
        c.Visibility = Input.Visibility;
        c.SupersedesId = Input.SupersedesId;

        if (existing == null)
        {
            c.Id = circulars.Create(c);
            audit.Log("Circular.Created", "Circular", c.Id, $"{c.CircularNumber}: {c.Title}");
        }
        else
        {
            circulars.UpdateMetadata(c, User.UserName());
            audit.Log("Circular.MetadataEdited", "Circular", c.Id, $"{c.CircularNumber}: {c.Title}; visibility={c.Visibility}");
        }
        if (stored != null)
        {
            var v = circulars.AddFile(c.Id, stored, User.UserName());
            audit.Log("Circular.FileUploaded", "Circular", c.Id, $"{c.CircularNumber} v{v}: {stored.OriginalFileName} ({stored.SizeBytes} bytes, SHA-256 {stored.Sha256})");
        }
        if (publish && (existing == null || existing.Status == CircularStatus.Draft))
        {
            var superseded = circulars.Publish(c.Id, User.UserName());
            audit.Log("Circular.Published", "Circular", c.Id, c.CircularNumber);
            if (superseded != null) audit.Log("Circular.Superseded", "Circular", superseded, $"Superseded by {c.CircularNumber}");
            Flash($"{c.CircularNumber} saved and published.");
        }
        else Flash($"{c.CircularNumber} saved" + (c.Status == CircularStatus.Draft ? " as a draft." : "."));
        return Redirect($"/Circulars/Details/{c.Id}");
    }
}
