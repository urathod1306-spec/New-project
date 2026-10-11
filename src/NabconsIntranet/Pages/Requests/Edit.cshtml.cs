using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Requests;

public class EditModel(RequestWorkflow workflow, UserRepository users, MasterRepository masters, FileStore files, AuditService audit) : AppPage
{
    public sealed class InputModel
    {
        [Required, StringLength(200)] public string Subject { get; set; } = "";
        [Required, Display(Name = "Request category")] public string? RequestCategory { get; set; }
        [Range(0, 999_999_999), Display(Name = "Amount involved (₹, if any)")] public decimal? Amount { get; set; }
        [Display(Name = "Required by")] public DateOnly? RequiredBy { get; set; }
        [Required, StringLength(5000), Display(Name = "Details and justification")] public string Details { get; set; } = "";
        [Required(ErrorMessage = "Select the Reviewing Officer."), Display(Name = "Reviewing Officer (level 1)")] public long? ReviewerUserId { get; set; }
        [Required(ErrorMessage = "Select the Sanctioning Authority."), Display(Name = "Sanctioning Authority (level 2)")] public long? ApproverUserId { get; set; }
        public long RowVersion { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public List<IFormFile> Uploads { get; set; } = new();
    public long Id { get; private set; }
    public ApprovalRequest? Existing { get; private set; }
    public List<AppUser> Approvers { get; private set; } = new();
    public List<string> Categories { get; private set; } = new();
    public List<Attachment> CurrentAttachments { get; private set; } = new();
    public string Accept => files.AcceptAttribute;
    public string AllowedList => files.AllowedList;

    private void Load()
    {
        Approvers = users.ActiveApprovers(User.UserId());
        Categories = masters.Names(MasterTypes.RequestCategory);
        if (Id > 0) CurrentAttachments = workflow.Attachments(Id);
    }

    public IActionResult OnGet(long? id)
    {
        Id = id ?? 0;
        if (id != null)
        {
            Existing = workflow.Get(id.Value);
            if (Existing == null || !workflow.CanView(Existing, User)) return NotFound();
            if (!workflow.CanEdit(Existing, User)) return Redirect($"/Requests/Details/{id}");
            Input = new InputModel
            {
                Subject = Existing.Subject, RequestCategory = Existing.RequestCategory, Amount = (decimal?)Existing.Amount,
                RequiredBy = ParseDate(Existing.RequiredBy), Details = Existing.Details, ReviewerUserId = Existing.ReviewerUserId,
                ApproverUserId = Existing.ApproverUserId, RowVersion = Existing.RowVersion,
            };
        }
        Load();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long? id, bool submit = false)
    {
        Id = id ?? 0;
        if (id != null)
        {
            Existing = workflow.Get(id.Value);
            if (Existing == null || !workflow.CanView(Existing, User)) return NotFound();
            if (!workflow.CanEdit(Existing, User)) return Forbid();
        }
        Load();
        if (Uploads.Count > 5) ModelState.AddModelError(nameof(Uploads), "Attach at most 5 files at a time.");
        if (!ModelState.IsValid) return Page();

        var stored = new List<StoredFile>();
        foreach (var f in Uploads.Where(u => u.Length > 0 || !string.IsNullOrEmpty(u.FileName)))
        {
            try { stored.Add(await files.SaveAsync(f, "requests")); }
            catch (UploadRejectedException ex)
            {
                audit.Log("Upload.Rejected", "Request", id, ex.Message);
                ModelState.AddModelError(nameof(Uploads), $"{f.FileName}: {ex.Message}");
                return Page();
            }
        }

        var r = new ApprovalRequest
        {
            Id = Id, Subject = Input.Subject.Trim(), RequestCategory = Clean(Input.RequestCategory, 100),
            Amount = Input.Amount.HasValue ? (double)Input.Amount.Value : null, RequiredBy = IsoDate(Input.RequiredBy),
            Details = Input.Details.Trim(), ReviewerUserId = Input.ReviewerUserId, ApproverUserId = Input.ApproverUserId,
        };
        try
        {
            if (Id == 0) Id = workflow.CreateDraft(r, User);
            else workflow.UpdateDraft(r, User, Input.RowVersion);
            foreach (var s in stored) workflow.AddAttachment(Id, s, User);
            if (submit)
            {
                var latest = workflow.Get(Id)!;
                workflow.Execute(Id, RequestWorkflow.Submit.Name, null, latest.RowVersion, User);
                Flash($"{latest.RequestNumber} submitted to {latest.ReviewerName}.");
            }
            else Flash("Draft saved. Submit it when ready.");
        }
        catch (WorkflowException ex)
        {
            ModelState.AddModelError("", ex.Message);
            if (Id > 0 && id == null) return Redirect($"/Requests/Edit/{Id}");
            return Page();
        }
        return Redirect($"/Requests/Details/{Id}");
    }
}
