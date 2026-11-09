using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.Inward;

/// <summary>One inward item (DD-08 §6.5): details masked per E8, invoice panel, trail, and the actions of §3.2–3.3 for this user.</summary>
public class ItemModel(InwardRepository repo, InwardWorkflow workflow, InwardOptions o, ENoteOptions enote, MasterRepository masters, InwardReports reports) : InwardPage
{
    public InwardItem I { get; private set; } = new();
    public InvoiceDetail? Inv { get; private set; }
    public List<InvoicePayment> Payments { get; private set; } = new();
    public List<InvoiceAdjustment> Adjustments { get; private set; } = new();
    public List<InwardMovement> Movements { get; private set; } = new();
    public List<InwardNote> Notes { get; private set; } = new();
    public List<PersonOption> Copies { get; private set; } = new();
    public List<InwardItem> Reminders { get; private set; } = new();
    public InwardItem? Linked { get; private set; }
    public List<PersonOption> Users { get; private set; } = new();
    public List<string> ENoteCategories { get; private set; } = new();

    public bool Full, IsHolder, IsOp, IsViewer, IsAccounts, IsInwarder, CanMark, CanReturn, CanCopy, CanNote, CanClose, CanConvert, CanEdit, CanCancel, CanReopen, CanAck;
    public bool CanCertify, CanSend, CanSendBack, CanPay, CanHold, CanRelease, CanReturnVendor, HasPayments;
    public InwardOptions Options => o;

    private IActionResult? Load(long id)
    {
        var it = repo.Item(id, User);
        if (it == null || !Access.CanView(it, User)) return NotFound();
        I = it;
        var uid = User.UserId();
        Full = it.Full;
        IsHolder = it.HolderUserId == uid && it.Status == ItemStatus.WithHolder;
        IsOp = Access.IsOperatorOf(User, it.OfficeId);
        IsViewer = Access.IsViewer(User);
        IsAccounts = Access.IsAccounts(User);
        IsInwarder = it.InwardedByUserId == uid;
        if (it.IsInvoice)
        {
            Inv = repo.Invoice(id);
            Payments = repo.Payments(id);
            Adjustments = repo.Adjustments(id);
        }
        HasPayments = Payments.Count > 0;
        Movements = repo.Movements(id);
        Notes = Full || (IsOp && !it.IsConfidential) ? repo.Notes(id) : new();
        Copies = repo.Copies(id);
        Reminders = repo.Reminders(id, User);
        if (it.LinkedToId is long l && repo.Item(l, User) is { } li && Access.CanView(li, User)) Linked = li;

        var s = Inv?.InvoiceStatus;
        var onHold = s == InvStatus.OnHold;
        CanMark = !onHold && ((it.Status == ItemStatus.Unassigned && (IsOp || IsViewer)) || (it.Status == ItemStatus.WithHolder && (IsHolder || IsOp || IsViewer)));
        CanReturn = IsHolder && !onHold;
        CanCopy = it.IsOpen && (IsHolder || IsOp) && s != InvStatus.OnHold;
        CanNote = Full || (IsOp && !it.IsConfidential);
        CanClose = IsHolder && !it.IsInvoice;
        CanConvert = CanClose && enote.Enabled;
        CanEdit = it.IsOpen && (((IsOp || IsInwarder || IsHolder) && (s == null || s is InvStatus.Received or InvStatus.WithVerifier))
                               || (IsAccounts && Inv != null && !HasPayments && !InvStatus.IsFinal(Inv.InvoiceStatus) && Inv.InvoiceStatus != InvStatus.OnHold));
        CanCancel = it.IsOpen && (IsOp || IsInwarder) && !HasPayments && s != InvStatus.OnHold;
        CanReopen = !it.IsOpen && !it.IsInvoice && it.ClosureType is not (Closure.Cancelled or Closure.Reminder or Closure.ConvertedToENote)
                    && (it.LastHolderUserId == uid || IsOp || IsViewer);
        CanAck = IsOp || IsViewer || IsInwarder;
        if (Inv != null)
        {
            CanCertify = IsHolder && s == InvStatus.WithVerifier && !IsInwarder;
            CanSend = IsHolder && s == InvStatus.Certified;
            CanSendBack = IsAccounts && s == InvStatus.SentToAccounts && !HasPayments;
            CanPay = IsAccounts && s is InvStatus.SentToAccounts or InvStatus.PartPaid;
            bool Handles(string? st) => st switch
            {
                InvStatus.Received => IsOp, InvStatus.WithVerifier or InvStatus.Certified => it.HolderUserId == uid,
                InvStatus.SentToAccounts or InvStatus.PartPaid => IsAccounts, _ => false,
            };
            CanHold = !onHold && Handles(s);
            CanRelease = onHold && Handles(Inv.HoldFromStatus);
            CanReturnVendor = !HasPayments && s != InvStatus.PartPaid && Handles(s);
        }
        if (CanMark || CanCopy) Users = repo.ActiveUsers();
        if (CanConvert) ENoteCategories = masters.Names(ENoteMaster.Category);
        return null;
    }

    public IActionResult OnGet(long id) => Load(id) ?? Page();

    public async Task<IActionResult> OnGetAckAsync(long id)
    {
        if (Load(id) is { } r) return r;
        if (!CanAck) return NotFound();
        return await Download(reports.Acknowledgement(I, User), "pdf");
    }

    private IActionResult Act(long id, Action action, string done)
    {
        if (Load(id) is { } r) return r;
        try { action(); Flash(done); }
        catch (WorkflowException ex) { FlashError(ex.Message); }
        return Redirect($"/Inward/Item/{id}");
    }

    public IActionResult OnPostAccept(long id, long rv) => Act(id, () => workflow.Accept(id, rv, User), "Accepted.");
    public IActionResult OnPostMark(long id, long to, string? remarks, long rv) => Act(id, () => workflow.Mark(id, to, remarks, rv, User), "Marked and notified.");
    public IActionResult OnPostReturn(long id, string? remarks, long rv) => Act(id, () => workflow.ReturnToRegistry(id, remarks, rv, User), "Returned to the registry.");
    public IActionResult OnPostCopy(long id, List<long>? users, bool confirm, long rv) => Act(id, () => workflow.CopyTo(id, users ?? new(), confirm, rv, User), "Copied and notified.");
    public IActionResult OnPostNote(long id, string? text) => Act(id, () => workflow.AddNote(id, text, User), "Note added.");
    public IActionResult OnPostClose(long id, CloseInput c, long rv) => Act(id, () => workflow.Close(id, c, rv, User), "Item closed.");
    public IActionResult OnPostCancel(long id, string? reason, long rv) => Act(id, () => workflow.Cancel(id, reason, rv, User), "Item cancelled (entered in error).");
    public IActionResult OnPostReopen(long id, string? reason, long rv) => Act(id, () => workflow.Reopen(id, reason, rv, User), "Item reopened.");

    public IActionResult OnPostConvert(long id, string? category, bool declaration, string? remarks, long rv)
    {
        if (Load(id) is { } r) return r;
        try
        {
            var note = workflow.ConvertToENote(id, category, declaration, remarks, rv, User);
            Flash("e-Note draft created. Complete it and send it on its way; the inward item is closed.");
            return Redirect($"/ENotes/{note}");
        }
        catch (WorkflowException ex) { FlashError(ex.Message); return Redirect($"/Inward/Item/{id}"); }
    }

    public IActionResult OnPostCertify(long id, string? on, long rv) => Act(id, () => workflow.Certify(id, on, rv, User), "Invoice certified.");
    public IActionResult OnPostSend(long id, long rv) => Act(id, () => workflow.SendToAccounts(id, rv, User), "Sent to Accounts.");
    public IActionResult OnPostSendBack(long id, string? remarks, long rv) => Act(id, () => workflow.SendBack(id, remarks, rv, User), "Sent back.");
    public IActionResult OnPostPayment(long id, PaymentInput p, long rv) => Act(id, () => workflow.RecordPayment(id, p, rv, User), "Payment recorded.");
    public IActionResult OnPostAdjust(long id, AdjustmentInput a, long rv) => Act(id, () => workflow.RecordAdjustment(id, a, rv, User), "Credit / debit note recorded.");
    public IActionResult OnPostCloseShort(long id, string? reason, long rv) => Act(id, () => workflow.CloseShort(id, reason, rv, User), "Invoice closed short.");
    public IActionResult OnPostReverse(long id, long paymentId, string? reason, long rv) => Act(id, () => workflow.Reverse(id, paymentId, reason, rv, User), "Payment reversed.");
    public IActionResult OnPostHold(long id, string? reason, long rv) => Act(id, () => workflow.Hold(id, reason, rv, User), "Invoice put on hold.");
    public IActionResult OnPostRelease(long id, long rv) => Act(id, () => workflow.Release(id, rv, User), "Invoice released.");
    public IActionResult OnPostReturnVendor(long id, string? reason, bool reject, long rv) =>
        Act(id, () => workflow.ReturnOrReject(id, reject, reason, rv, User), reject ? "Invoice rejected." : "Invoice returned to the vendor.");
}
