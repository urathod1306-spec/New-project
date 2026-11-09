using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Inward;

/// <summary>Edit details (DD-08 §3.2; R2-6, R2-15). Type, office and number never change; a masked operator edits metadata only.</summary>
public class EditModel(InwardRepository repo, InwardWorkflow workflow, InwardOptions o, MasterRepository masters, Fmt fmt) : InwardPage
{
    [BindProperty] public InwardInput Input { get; set; } = new();
    public InwardItem I { get; private set; } = new();
    public InwardFormModel Form { get; private set; } = new();
    public List<string> Warnings { get; private set; } = new();
    public bool Blocked { get; private set; }
    public string? Error { get; private set; }

    private IActionResult? Load(long id, bool fill)
    {
        var it = repo.Item(id, User);
        if (it == null || !Access.CanView(it, User) || !it.IsOpen) return NotFound();
        I = it;
        var inv = it.IsInvoice ? repo.Invoice(id) : null;
        var general = Access.IsOperatorOf(User, it.OfficeId) || it.InwardedByUserId == User.UserId() || (it.HolderUserId == User.UserId() && it.Status == ItemStatus.WithHolder);
        if (inv != null && inv.InvoiceStatus is not (InvStatus.Received or InvStatus.WithVerifier)) general = false;
        var amountsOnly = !general && inv != null && Access.IsAccounts(User) && repo.Payments(id).Count == 0 && !InvStatus.IsFinal(inv.InvoiceStatus);
        if (!general && !amountsOnly) return NotFound();
        if (fill)
        {
            var local = DateTime.Parse(it.ReceivedAt, null, System.Globalization.DateTimeStyles.AdjustToUniversal).AddMinutes(fmt.OffsetMinutes);
            Input = new InwardInput
            {
                ItemType = it.ItemType, Mode = it.Mode, ReceivedOn = local.ToString("yyyy-MM-dd"), SenderName = it.SenderName, SenderOrg = it.SenderOrg, SenderRef = it.SenderRef,
                SenderRefDate = it.SenderRefDate, Subject = it.Subject, Summary = it.Summary, Category = it.Category, Priority = it.Priority, IsConfidential = it.IsConfidential,
                ActionRequired = it.ActionRequired, ReplyDueOn = it.ReplyDueOn, KeptAt = it.KeptAt, Pages = it.Pages, Irn = it.Irn,
            };
            if (inv != null && !(general && it.IsConfidential && !it.Full))     // a masked editor never sees the invoice part (E8)
                Input.Invoice = new InvoiceInput
                {
                    VendorName = inv.VendorName, VendorGstin = inv.VendorGstin, IsMsme = inv.IsMsme, IsTrader = inv.IsTrader, UdyamNo = inv.UdyamNo, MsmeClass = inv.MsmeClass,
                    InvoiceNo = inv.InvoiceNo, InvoiceDate = inv.InvoiceDate, ProjectCode = inv.ProjectCode, PoRef = inv.PoRef, Taxable = Paise.Plain(inv.TaxablePaise),
                    Gst = Paise.Plain(inv.GstPaise), Other = Paise.Plain(inv.OtherPaise), Total = Paise.Plain(inv.TotalPaise), TermsDays = (int)inv.TermsDays,
                    NotDuplicate = inv.OverrideReason != null, OverrideReason = it.Full ? inv.OverrideReason : null,
                };
        }
        Input.ItemType = it.ItemType;
        var codes = repo.ProjectCodes(true);
        if (inv?.ProjectCode is string pc && !codes.Any(c => string.Equals(c.Code, pc, StringComparison.OrdinalIgnoreCase)))
            codes.Insert(0, new ProjectCode { Code = pc, Name = "(inactive)" });
        Form = new InwardFormModel
        {
            Input = Input, Edit = true, MetadataOnly = general && it.IsConfidential && !it.Full, ShowInvoice = inv != null && !(general && it.IsConfidential && !it.Full), AmountsOnly = amountsOnly, Blocked = Blocked,
            Categories = masters.Names(MasterTypes.InwardCategory), ProjectCodes = codes, MaxBackdateDays = o.MaxBackdateDays, DefaultTerms = o.InvoiceTermsDays,
            ReplyDaysHint = $"{o.ReplyDueDaysNormal} days (Normal), {o.ReplyDueDaysUrgent} (Urgent), {o.ReplyDueDaysImmediate} (Immediate)",
        };
        return null;
    }

    public IActionResult OnGet(long id) => Load(id, true) ?? Page();

    public IActionResult OnPost(long id, long rv, bool confirmed)
    {
        if (Load(id, false) is { } r) return r;
        try
        {
            workflow.Edit(id, Input, rv, User, confirmed);
            Flash("Details saved.");
            return Redirect($"/Inward/Item/{id}");
        }
        catch (InwardConfirmException ex) { Warnings = ex.Warnings; Blocked = ex.Blocked; }
        catch (WorkflowException ex) { Error = ex.Message; }
        return Load(id, false) ?? Page();
    }
}
