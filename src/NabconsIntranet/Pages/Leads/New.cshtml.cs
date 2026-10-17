using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Leads;

public class NewModel(LeadRepository leads, MasterRepository masters, FileStore files, AuditService audit, IntranetOptions o) : AppPage
{
    public sealed class InputModel
    {
        [Required, StringLength(250), Display(Name = "Lead title")] public string Title { get; set; } = "";
        [Required, StringLength(200), Display(Name = "Client / organisation")] public string ClientName { get; set; } = "";
        [Display(Name = "Client type")] public string? ClientType { get; set; }
        [Display(Name = "Sector / domain")] public string? Sector { get; set; }
        [StringLength(100), Display(Name = "State / location of assignment")] public string? StateLocation { get; set; }
        [Display(Name = "Source of lead")] public string? Source { get; set; }
        [Range(0, 1_000_000), Display(Name = "Estimated fee (₹ lakh)")] public decimal? EstimatedValueLakh { get; set; }
        [Display(Name = "Bid / proposal due date")] public DateOnly? DueDate { get; set; }
        [StringLength(120), Display(Name = "Client contact person")] public string? ContactName { get; set; }
        [StringLength(120), Display(Name = "Designation")] public string? ContactDesignation { get; set; }
        [StringLength(30), Display(Name = "Phone")] public string? ContactPhone { get; set; }
        [StringLength(150), Display(Name = "E-mail")] public string? ContactEmail { get; set; }
        [Required, StringLength(8000), Display(Name = "Description / scope summary")] public string Description { get; set; } = "";
        [StringLength(100), Display(Name = "Tender / reference number")] public string? TenderNumber { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public List<IFormFile> Uploads { get; set; } = new();
    [BindProperty] public bool ConfirmDuplicate { get; set; }
    public List<Lead> Duplicates { get; private set; } = new();
    public List<string> ClientTypes { get; private set; } = new();
    public List<string> Sectors { get; private set; } = new();
    public List<string> Sources { get; private set; } = new();
    public string Accept => string.Join(",", FileStore.LeadExtensions);

    private void Load()
    {
        ClientTypes = masters.Names(MasterTypes.ClientType);
        Sectors = masters.Names(MasterTypes.LeadSector);
        Sources = masters.Names(MasterTypes.LeadSource);
    }

    public void OnGet() => Load();

    /// <summary>Soft duplicate check used by the form before submitting (shows reference and status only).</summary>
    public IActionResult OnGetCheck(string? client, string? title, string? tender)
    {
        if (string.IsNullOrWhiteSpace(client) || string.IsNullOrWhiteSpace(title)) return new JsonResult(Array.Empty<object>());
        return new JsonResult(leads.PossibleDuplicates(client, title, tender)
            .Select(l => new { reference = l.Reference, status = l.Status, submitted = l.CreatedAt[..10] }));
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Load();
        string? Pick(string? v, List<string> list, string field)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            if (!list.Contains(v)) ModelState.AddModelError("Input." + field, "Choose a value from the list.");
            return v;
        }
        Pick(Input.ClientType, ClientTypes, nameof(Input.ClientType));
        Pick(Input.Sector, Sectors, nameof(Input.Sector));
        Pick(Input.Source, Sources, nameof(Input.Source));
        if (!string.IsNullOrWhiteSpace(Input.ContactEmail) && !MailAddress.TryCreate(Input.ContactEmail.Trim(), out _))
            ModelState.AddModelError("Input.ContactEmail", "Enter a valid e-mail address.");
        if (!string.IsNullOrWhiteSpace(Input.ContactPhone) && !Input.ContactPhone.All(ch => char.IsDigit(ch) || ch is ' ' or '+' or '-' or '(' or ')'))
            ModelState.AddModelError("Input.ContactPhone", "Use digits only (spaces, + - ( ) allowed).");
        var incoming = Uploads.Where(f => f.Length > 0 || !string.IsNullOrEmpty(f.FileName)).ToList();
        if (incoming.Count > 10) ModelState.AddModelError(nameof(Uploads), "Attach at most 10 files.");
        if (incoming.Sum(f => f.Length) > o.MaxLeadTotalMB * 1024L * 1024L) ModelState.AddModelError(nameof(Uploads), $"Attachments may total at most {o.MaxLeadTotalMB} MB per lead.");
        if (!ModelState.IsValid) return Page();

        if (!ConfirmDuplicate)
        {
            Duplicates = leads.PossibleDuplicates(Input.ClientName, Input.Title, Input.TenderNumber);
            if (Duplicates.Count > 0) return Page(); // the warning is shown; the user confirms to continue
        }

        // validate and store every file before creating the lead, so a rejected file creates nothing
        var stored = new List<StoredFile>();
        foreach (var f in incoming)
        {
            try { stored.Add(await files.SaveAsync(f, "leads", FileStore.LeadExtensions, o.MaxLeadFileMB * 1024L * 1024L)); }
            catch (UploadRejectedException ex)
            {
                audit.Log("Upload.Rejected", "Lead", null, $"{f.FileName}: {ex.Message}");
                ModelState.AddModelError(nameof(Uploads), $"{f.FileName}: {ex.Message}");
                return Page();
            }
        }

        var lead = leads.Create(new Lead
        {
            Title = Input.Title.Trim(), ClientName = Input.ClientName.Trim(), ClientType = Clean(Input.ClientType, 100), Sector = Clean(Input.Sector, 100),
            StateLocation = Clean(Input.StateLocation, 100), Source = Clean(Input.Source, 100),
            EstimatedValueLakh = Input.EstimatedValueLakh.HasValue ? (double)Input.EstimatedValueLakh.Value : null, DueDate = IsoDate(Input.DueDate),
            ContactName = Clean(Input.ContactName, 120), ContactDesignation = Clean(Input.ContactDesignation, 120), ContactPhone = Clean(Input.ContactPhone, 30),
            ContactEmail = Clean(Input.ContactEmail, 150), Description = Input.Description.Trim(), TenderNumber = Clean(Input.TenderNumber, 100),
        }, User);
        foreach (var s in stored) leads.AddAttachment(lead, s, "Submitter", User);
        if (ConfirmDuplicate) audit.Log("Lead.DuplicateConfirmed", "Lead", lead.Id, $"{lead.Reference} submitted despite possible duplicates");
        Flash($"Thank you. Your lead has been submitted with reference {lead.Reference}.");
        return Redirect($"/Leads/Details/{lead.Id}");
    }
}
