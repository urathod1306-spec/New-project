using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Pages.Compliance;

/// <summary>
/// Print an Action Taken Report (DD-07 §3.4, §6, §7): choose the template, events and as-on date → preview with the
/// content hash → draft downloads (DOCX, PDF, XLSX) → Compliance Admins mark it final against the previewed hash.
/// </summary>
public class AtrModel(ComplianceRepository repo, AtrBuilder atr, ComplianceOptions o, AuditService audit) : CompliancePage
{
    [BindProperty(SupportsGet = true)] public string? Template { get; set; }
    [BindProperty(SupportsGet = true)] public List<long> Events { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? AsOn { get; set; }
    [BindProperty(SupportsGet = true)] public string? Since { get; set; }
    [BindProperty(SupportsGet = true)] public string? Officer { get; set; }

    public List<ComplianceEvent> Choices { get; private set; } = new();
    public AtrBuild? Build { get; private set; }
    public string? Error { get; private set; }
    public bool IsAdmin { get; private set; }
    public List<MasterItem> Forums { get; private set; } = new();

    private AtrOptions Options() => new()
    {
        Template = Template ?? AtrTemplate.BoardAtr, EventIds = Events, AsOn = AsOn ?? F.Today.ToString("yyyy-MM-dd"),
        CompletedSince = Since, OfficerCodeFormat = Officer ?? o.DefaultOfficerCodeFormat,
    };

    private Fmt F => HttpContext.RequestServices.GetRequiredService<Fmt>();

    private IActionResult? Load()
    {
        if (!Access.SeesAll(User)) return NotFound();
        IsAdmin = Access.IsAdmin(User);
        Choices = repo.Events(User, null, null, null).Where(e => Access.CanSeeEventManaged(e, User)).ToList();
        Forums = HttpContext.RequestServices.GetRequiredService<MasterRepository>().List(MasterTypes.ComplianceForum, false);
        Events = Events.Where(id => Choices.Any(c => c.Id == id)).Distinct().ToList();
        if (Template == null && Events.Count > 0) Template = ComplianceSource.Template(Choices.First(c => c.Id == Events[0]).SourceType);
        AsOn ??= F.Today.ToString("yyyy-MM-dd");
        Officer ??= o.DefaultOfficerCodeFormat;
        // default only when the form has not been submitted yet (an empty field means "none" and must stay so for Mark as final)
        if (Since == null && Events.Count > 0 && HttpMethods.IsGet(Request.Method) && Request.Query["since"].Count == 0)
        {
            var newest = Choices.Where(c => Events.Contains(c.Id)).OrderByDescending(c => c.DateKey ?? "").First();
            Since = atr.DefaultCompletedSince(Template ?? AtrTemplate.BoardAtr, newest.ForumId, AsOn);
        }
        return null;
    }

    public IActionResult OnGet(bool preview = false)
    {
        if (Load() is { } r) return r;
        if (preview && Events.Count > 0)
        {
            try { Build = atr.Build(Options(), User); }
            catch (WorkflowException ex) { Error = ex.Message; }
        }
        return Page();
    }

    /// <summary>Ticks a forum's newest event plus its earlier events that still have open points.</summary>
    public IActionResult OnGetPack(long forumId)
    {
        if (Load() is { } r) return r;
        var ofForum = Choices.Where(c => c.ForumId == forumId).ToList();
        if (ofForum.Count == 0) { FlashError("No events for that forum."); return RedirectToPage(); }
        var picked = ofForum.Take(1).Concat(ofForum.Skip(1).Where(c => c.OpenCount > 0)).Select(c => c.Id).ToList();
        var src = ofForum[0].SourceType;
        return Redirect("/Compliance/Atr?" + string.Join("&", picked.Select(id => "events=" + id)) + "&template=" + ComplianceSource.Template(src));
    }

    public async Task<IActionResult> OnGetDownloadAsync(string format)
    {
        if (Load() is { } r) return r;
        if (!ReportFormat.All.Contains(format)) return NotFound();
        AtrBuild b;
        try { b = atr.Build(Options(), User); }
        catch (WorkflowException ex) { FlashError(ex.Message); return Redirect("/Compliance/Atr" + QueryString(false)); }
        audit.Log("Compliance.AtrDownloaded", "ComplianceEvent", null, $"draft; {b.Options.Template}; events {string.Join(",", b.Options.EventIds)}; {b.Items.Count} rows; {format}; content {b.Hash[..16]}");
        return await Download(atr.DraftDocument(b), format);
    }

    public IActionResult OnPostFinal(string? hash, string? submittedTo, string? submittedOn)
    {
        if (Load() is { } r) return r;
        if (!IsAdmin) return NotFound();
        try
        {
            var id = atr.Finalise(Options(), hash ?? "", submittedTo, submittedOn, User);
            Flash("Marked as final. Download the copy for submission from this page; it carries the snapshot number and SHA-256.");
            return Redirect($"/Compliance/Snapshot/{id}");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); return Redirect("/Compliance/Atr" + QueryString(true)); }
    }

    public string QueryString(bool preview, string? handler = null, string? format = null)
    {
        var q = new List<string>();
        void Add(string k, string? v) { if (!string.IsNullOrEmpty(v)) q.Add(k + "=" + Uri.EscapeDataString(v)); }
        if (handler != null) Add("handler", handler);
        if (format != null) Add("format", format);
        Add("template", Template);
        foreach (var e in Events) q.Add("events=" + e);
        Add("asOn", AsOn); q.Add("since=" + Uri.EscapeDataString(Since ?? "")); Add("officer", Officer);
        if (preview) q.Add("preview=true");
        return "?" + string.Join("&", q);
    }
}
