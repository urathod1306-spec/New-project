using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

/// <summary>The invoice part of the inward form and of the edit form (DD-08 §6.3). Amounts are text in rupees.</summary>
public sealed class InvoiceInput
{
    public string? VendorName { get; set; }
    public string? VendorGstin { get; set; }
    public bool IsMsme { get; set; }
    public bool IsTrader { get; set; }
    public string? UdyamNo { get; set; }
    public string? MsmeClass { get; set; }
    public string? InvoiceNo { get; set; }
    public string? InvoiceDate { get; set; }
    public string? ProjectCode { get; set; }
    public string? PoRef { get; set; }
    public string? Taxable { get; set; }
    public string? Gst { get; set; }
    public string? Other { get; set; }
    public string? Total { get; set; }
    public int? TermsDays { get; set; }
    /// <summary>"This is not a duplicate" — with <see cref="OverrideReason"/>, saves an invoice whose key already exists.</summary>
    public bool NotDuplicate { get; set; }
    public string? OverrideReason { get; set; }
}

public sealed class PaymentInput
{
    public string? PaidOn { get; set; }
    public string? Net { get; set; }
    public string? Tds { get; set; }
    public string? OtherDeductions { get; set; }
    public string? VoucherRef { get; set; }
    public string? Remarks { get; set; }
    /// <summary>"Checked: not a duplicate payment" — needed for the first payment of an overridden duplicate.</summary>
    public bool CheckedNotDuplicate { get; set; }
}

public sealed class AdjustmentInput
{
    public string? Kind { get; set; }        // CreditNote | DebitNote
    public string? DocNo { get; set; }
    public string? DocDate { get; set; }
    public string? Amount { get; set; }
    public string? Remarks { get; set; }
    /// <summary>Optional: the inward number of the credit / debit note item, closed when the adjustment is recorded (R2-1).</summary>
    public string? NoteNumber { get; set; }
}

/// <summary>Pure invoice rules (DD-08 §3.4, §3.5), kept static so they can be tested on their own.</summary>
public static class InvoiceRules
{
    public static readonly string[] MsmeClasses = { "Micro", "Small", "Medium" };
    private static readonly string[] Suffixes = { "pvt", "private", "ltd", "limited", "llp", "co" };

    /// <summary>Lower case, letters and digits only, with company suffixes removed ("Acme Pvt. Ltd." → "acme").</summary>
    public static string NormVendor(string name)
    {
        var words = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in name.ToLowerInvariant() + " ")
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
            else if (ch is ' ' or ',' or '-' or '(' or ')' or '&' or '/' && sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        while (words.Count > 1 && Suffixes.Contains(words[^1])) words.RemoveAt(words.Count - 1);
        return string.Concat(words);
    }

    /// <summary>Upper case, letters and digits only ("inv/24-25/0012" → "INV24250012").</summary>
    public static string NormInvoiceNo(string no) => new(no.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static string KeyPrefix(string? gstin, string vendor, string invoiceNo) =>
        (string.IsNullOrEmpty(gstin) ? "N:" + NormVendor(vendor) : gstin.ToUpperInvariant()) + "|" + NormInvoiceNo(invoiceNo) + "|";

    public static string DupKey(string? gstin, string vendor, string invoiceNo, string invoiceFy) => KeyPrefix(gstin, vendor, invoiceNo) + invoiceFy;

    /// <summary>Payment due date (D15): invoice date + terms; for an MSME invoice, + min(terms, MSME limit).</summary>
    public static string DueOn(string invoiceDate, long terms, bool isMsme, int msmeMaxDays) =>
        DateOnly.ParseExact(invoiceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays((int)(isMsme ? Math.Min(terms, msmeMaxDays) : terms))
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Status from the sums (§3.4): outstanding 0 → Paid; something settled → PartPaid; else SentToAccounts. Overpayment is refused.</summary>
    public static string StatusFor(long effective, long settled)
    {
        if (settled > effective) throw new WorkflowException($"This would overpay the invoice by {Paise.Format(settled - effective)}.");
        return settled == effective ? InvStatus.Paid : settled > 0 ? InvStatus.PartPaid : InvStatus.SentToAccounts;
    }
}

/// <summary>Invoice lifecycle (DD-08 §3.3–3.5): the second half of <see cref="InwardWorkflow"/>.</summary>
public sealed partial class InwardWorkflow
{
    private static InvoiceDetail? InvoiceOf(SqliteConnection c, SqliteTransaction tx, long id) =>
        Db.Query<InvoiceDetail>(c, tx, "SELECT * FROM InvoiceDetails WHERE ItemId = @id", new { id }).FirstOrDefault();

    // ================================================================= validation and duplicates

    /// <summary>Validates the invoice fields. With <paramref name="amountsOnly"/> (Invoice Accounts editing), only amounts and terms are taken from the input.</summary>
    private InvoiceDetail ValidateInvoice(InvoiceInput i, InvoiceDetail? amountsOnly)
    {
        var r = new InvoiceDetail();
        if (amountsOnly != null)
        {
            r.VendorName = amountsOnly.VendorName; r.VendorGstin = amountsOnly.VendorGstin; r.IsMsme = amountsOnly.IsMsme; r.IsTrader = amountsOnly.IsTrader;
            r.UdyamNo = amountsOnly.UdyamNo; r.MsmeClass = amountsOnly.MsmeClass; r.InvoiceNo = amountsOnly.InvoiceNo; r.InvoiceDate = amountsOnly.InvoiceDate;
            r.ProjectCode = amountsOnly.ProjectCode; r.PoRef = amountsOnly.PoRef; r.OverrideReason = amountsOnly.OverrideReason;
        }
        else
        {
            r.VendorName = T("Vendor name", i.VendorName, 200, true)!;
            var g = Gstin.Normalise(i.VendorGstin);
            if (g != null && Gstin.Problem(g) is string gp) throw new WorkflowException("GSTIN: " + gp);
            r.VendorGstin = g;
            r.IsMsme = i.IsMsme;
            r.IsTrader = i.IsMsme && i.IsTrader;
            r.UdyamNo = i.IsMsme ? T("Udyam number", i.UdyamNo?.ToUpperInvariant(), 30) : null;
            if (r.UdyamNo != null && !System.Text.RegularExpressions.Regex.IsMatch(r.UdyamNo, "^UDYAM-[A-Z]{2}-[0-9]{2}-[0-9]{7}$"))
                throw new WorkflowException("The Udyam number looks like UDYAM-MH-01-0001234.");
            r.MsmeClass = i.IsMsme && InvoiceRules.MsmeClasses.Contains(i.MsmeClass) ? i.MsmeClass : null;
            r.InvoiceNo = T("Invoice number", i.InvoiceNo, 50, true)!;
            if (InvoiceRules.NormInvoiceNo(r.InvoiceNo).Length == 0) throw new WorkflowException("The invoice number must contain letters or digits.");
            r.InvoiceDate = Date("Invoice date", i.InvoiceDate, true)!;
            if (string.CompareOrdinal(r.InvoiceDate, Today) > 0) throw new WorkflowException("The invoice date cannot be in the future.");
            if (string.CompareOrdinal(r.InvoiceDate, "2000-01-01") < 0) throw new WorkflowException("Check the invoice date.");
            var pc = T("Project code", i.ProjectCode, 50);
            if (pc != null)
            {
                pc = db.Scalar<string>("SELECT Code FROM ProjectCodes WHERE Code = @pc COLLATE NOCASE AND IsActive = 1", new { pc })
                     ?? throw new WorkflowException($"Project code {pc} is not in the active list.");
            }
            r.ProjectCode = pc;
            r.PoRef = T("PO reference", i.PoRef, 100);
            r.OverrideReason = i.NotDuplicate ? T("Reason it is not a duplicate", i.OverrideReason, 500, true) : null;
        }
        r.TaxablePaise = Paise.Parse(i.Taxable, "Taxable value");
        r.GstPaise = Paise.Parse(i.Gst, "GST");
        r.OtherPaise = Paise.Parse(i.Other, "Other charges");
        r.TotalPaise = Paise.Parse(i.Total, "Invoice total") ?? throw new WorkflowException("Enter the invoice total.");
        if (r.TotalPaise <= 0) throw new WorkflowException("The invoice total must be more than zero.");
        if ((r.TaxablePaise ?? r.GstPaise) != null)
        {
            var sum = (r.TaxablePaise ?? 0) + (r.GstPaise ?? 0) + (r.OtherPaise ?? 0);
            if (Math.Abs(sum - r.TotalPaise) > 100)
                throw new WorkflowException($"Taxable value + GST + other charges = {Paise.Format(sum)}, which differs from the total {Paise.Format(r.TotalPaise)} by more than ₹ 1.");
        }
        var terms = i.TermsDays ?? amountsOnly?.TermsDays ?? o.InvoiceTermsDays;
        if (terms is < 0 or > 365) throw new WorkflowException("Payment terms must be between 0 and 365 days.");
        r.TermsDays = terms;
        r.InvoiceFy = LeadRepository.FinancialYear(DateOnly.ParseExact(r.InvoiceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture));
        r.DupKey = InvoiceRules.DupKey(r.VendorGstin, r.VendorName, r.InvoiceNo, r.InvoiceFy);
        r.PaymentDueOn = InvoiceRules.DueOn(r.InvoiceDate, r.TermsDays, r.IsMsme, o.MsmeMaxDays);
        return r;
    }

    private sealed class DupHit
    {
        public long Id { get; set; }
        public string Number { get; set; } = "";
        public string? UdyamNo { get; set; }
        public string? MsmeClass { get; set; }
        public string InvoiceDate { get; set; } = "";
    }

    /// <summary>
    /// Duplicate check (§3.5). An existing key blocks unless an override reason is given (then the reason is kept and the
    /// invoice flagged); otherwise warnings are returned unless <paramref name="confirmed"/>.
    /// </summary>
    private List<string> DuplicateCheck(SqliteConnection c, SqliteTransaction tx, InvoiceDetail inv, long? excludeId, bool confirmed, ClaimsPrincipal u)
    {
        var ex = excludeId ?? 0;
        // numbers of matching items are named only when the user may see them (E4: otherwise the same neutral answer)
        string Shown(IEnumerable<DupHit> hits)
        {
            var visible = hits.Select(h => Load(c, tx, h.Id)).Where(it => access.CanView(it, u)).Select(it => it.Number).ToList();
            return visible.Count > 0 ? string.Join(", ", visible) : "an item you cannot open (ask the registry)";
        }
        var same = Db.Query<DupHit>(c, tx, """
            SELECT i.Id, i.Number FROM InvoiceDetails d JOIN InwardItems i ON i.Id = d.ItemId
            WHERE d.DupKey = @k AND d.InvoiceStatus <> 'Cancelled' AND d.ItemId <> @ex ORDER BY i.Id LIMIT 3
            """, new { k = inv.DupKey, ex });
        if (same.Count > 0 && inv.OverrideReason == null)
            throw new InwardConfirmException(new() { $"This invoice is already inwarded as {Shown(same)} (same vendor, invoice number and financial year). If it is really a different invoice, tick \"This is not a duplicate\" and give the reason." }, true);
        if (same.Count == 0) inv.OverrideReason = null;          // nothing to override
        var w = new List<string>();
        if (confirmed) return w;
        // prefix range on the key (index-friendly, unlike LIKE): every key with the prefix sorts between "p" and "p" + U+FFFF
        var pfx = InvoiceRules.KeyPrefix(inv.VendorGstin, inv.VendorName, inv.InvoiceNo);
        var other = Db.Query<DupHit>(c, tx, """
                SELECT i.Id, i.Number, d.InvoiceDate FROM InvoiceDetails d JOIN InwardItems i ON i.Id = d.ItemId
                WHERE d.DupKey >= @p AND d.DupKey < @p2 AND d.DupKey <> @k AND d.InvoiceStatus <> 'Cancelled' AND d.ItemId <> @ex LIMIT 3
                """, new { p = pfx, p2 = pfx + "\uffff", k = inv.DupKey, ex });
        if (other.Count > 0) w.Add($"The same vendor and invoice number were inwarded for another financial year as {Shown(other)}.");
        if (inv.VendorGstin != null)
        {
            var from = DateOnly.ParseExact(inv.InvoiceDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var near = Db.Query<DupHit>(c, tx, """
                    SELECT i.Id, i.Number, d.InvoiceDate FROM InvoiceDetails d JOIN InwardItems i ON i.Id = d.ItemId
                    WHERE d.VendorGstin = @g AND d.TotalPaise = @t AND d.InvoiceDate BETWEEN @a AND @b AND d.DupKey <> @k AND d.InvoiceStatus <> 'Cancelled' AND d.ItemId <> @ex LIMIT 3
                    """, new { g = inv.VendorGstin, t = inv.TotalPaise, a = from.AddDays(-30).ToString("yyyy-MM-dd"), b = from.AddDays(30).ToString("yyyy-MM-dd"), k = inv.DupKey, ex });
            if (near.Count > 0) w.Add($"An invoice from the same GSTIN for the same total, dated within 30 days, is inwarded as {Shown(near)}.");
            if (!inv.IsMsme)
            {
                var m = Db.Query<DupHit>(c, tx, "SELECT i.Id, i.Number, d.UdyamNo, d.MsmeClass FROM InvoiceDetails d JOIN InwardItems i ON i.Id = d.ItemId WHERE d.VendorGstin = @g AND d.IsMsme = 1 AND d.ItemId <> @ex ORDER BY d.ItemId DESC LIMIT 1",
                    new { g = inv.VendorGstin, ex }).FirstOrDefault();
                // the Udyam number and class are shown only to users with full access to that earlier invoice
                if (m != null)
                    w.Add(access.HasFull(Load(c, tx, m.Id), u)
                        ? $"An earlier invoice from this GSTIN ({m.Number}) was marked MSME{(m.MsmeClass == null ? "" : " — " + m.MsmeClass)}{(m.UdyamNo == null ? "" : ", " + m.UdyamNo)}. Check whether this one is MSME too."
                        : "An earlier invoice from this GSTIN was marked MSME. Check whether this one is MSME too.");
            }
        }
        return w;
    }

    private static void InsertInvoice(SqliteConnection c, SqliteTransaction tx, long itemId, InvoiceDetail v, string status)
    {
        try
        {
            Db.Exec(c, tx, """
                INSERT INTO InvoiceDetails (ItemId, VendorName, VendorGstin, IsMsme, IsTrader, UdyamNo, MsmeClass, InvoiceNo, InvoiceDate, InvoiceFy, ProjectCode, PoRef,
                    TaxablePaise, GstPaise, OtherPaise, TotalPaise, TermsDays, PaymentDueOn, InvoiceStatus, DupKey, OverrideReason)
                VALUES (@itemId, @VendorName, @VendorGstin, @IsMsme, @IsTrader, @UdyamNo, @MsmeClass, @InvoiceNo, @InvoiceDate, @InvoiceFy, @ProjectCode, @PoRef,
                    @TaxablePaise, @GstPaise, @OtherPaise, @TotalPaise, @TermsDays, @PaymentDueOn, @status, @DupKey, @OverrideReason)
                """, new
            {
                itemId, v.VendorName, v.VendorGstin, v.IsMsme, v.IsTrader, v.UdyamNo, v.MsmeClass, v.InvoiceNo, v.InvoiceDate, v.InvoiceFy, v.ProjectCode, v.PoRef,
                v.TaxablePaise, v.GstPaise, v.OtherPaise, v.TotalPaise, v.TermsDays, v.PaymentDueOn, status, v.DupKey, v.OverrideReason,
            });
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        { throw new WorkflowException("This invoice was inwarded by someone else a moment ago. Search the register for it."); }
    }

    /// <summary>Writes changed invoice fields; returns their labels (for the movement, never the values).</summary>
    private static List<string> UpdateInvoice(SqliteConnection c, SqliteTransaction tx, InvoiceDetail old, InvoiceDetail v)
    {
        var changed = new List<string>();
        void C(object? a, object? b, string label) { if (!Equals(a, b)) changed.Add(label); }
        C(old.VendorName, v.VendorName, "vendor"); C(old.VendorGstin, v.VendorGstin, "GSTIN"); C(old.IsMsme, v.IsMsme, "MSME"); C(old.IsTrader, v.IsTrader, "trader");
        C(old.UdyamNo, v.UdyamNo, "Udyam no."); C(old.MsmeClass, v.MsmeClass, "MSME class"); C(old.InvoiceNo, v.InvoiceNo, "invoice no."); C(old.InvoiceDate, v.InvoiceDate, "invoice date");
        C(old.ProjectCode, v.ProjectCode, "project code"); C(old.PoRef, v.PoRef, "PO ref"); C(old.TaxablePaise, v.TaxablePaise, "taxable value"); C(old.GstPaise, v.GstPaise, "GST");
        C(old.OtherPaise, v.OtherPaise, "other charges"); C(old.TotalPaise, v.TotalPaise, "total"); C(old.TermsDays, v.TermsDays, "terms");
        C(old.OverrideReason != null, v.OverrideReason != null, "duplicate override");
        if (changed.Count == 0) return changed;
        try
        {
            Db.Exec(c, tx, """
                UPDATE InvoiceDetails SET VendorName = @VendorName, VendorGstin = @VendorGstin, IsMsme = @IsMsme, IsTrader = @IsTrader, UdyamNo = @UdyamNo, MsmeClass = @MsmeClass,
                       InvoiceNo = @InvoiceNo, InvoiceDate = @InvoiceDate, InvoiceFy = @InvoiceFy, ProjectCode = @ProjectCode, PoRef = @PoRef, TaxablePaise = @TaxablePaise,
                       GstPaise = @GstPaise, OtherPaise = @OtherPaise, TotalPaise = @TotalPaise, TermsDays = @TermsDays, PaymentDueOn = @PaymentDueOn, DupKey = @DupKey,
                       OverrideReason = @OverrideReason, DuplicateCheckedBy = CASE WHEN @OverrideReason IS NULL THEN NULL ELSE DuplicateCheckedBy END
                WHERE ItemId = @ItemId
                """, new
            {
                v.VendorName, v.VendorGstin, v.IsMsme, v.IsTrader, v.UdyamNo, v.MsmeClass, v.InvoiceNo, v.InvoiceDate, v.InvoiceFy, v.ProjectCode, v.PoRef, v.TaxablePaise,
                v.GstPaise, v.OtherPaise, v.TotalPaise, v.TermsDays, v.PaymentDueOn, v.DupKey, v.OverrideReason, old.ItemId,
            });
        }
        catch (SqliteException e) when (e.SqliteErrorCode == 19)
        { throw new WorkflowException("Another invoice with the same vendor, number and financial year already exists."); }
        return changed;
    }

    // ================================================================= transitions

    /// <summary>Loads an invoice item and its details, checking visibility.</summary>
    private (InwardItem it, InvoiceDetail inv) LoadInvoice(SqliteConnection c, SqliteTransaction tx, long id, ClaimsPrincipal u)
    {
        var it = LoadVisible(c, tx, id, u);
        if (!it.IsInvoice) throw new WorkflowException("This item is not an invoice.");
        return (it, InvoiceOf(c, tx, id) ?? throw new WorkflowException(NotFound));
    }

    private void RequireAccounts(ClaimsPrincipal u)
    {
        if (!access.IsAccounts(u)) throw new WorkflowException("Only Invoice Accounts can do this.");
    }

    private static void RequireStatus(InvoiceDetail inv, params string[] allowed)
    {
        if (!allowed.Contains(inv.InvoiceStatus))
            throw new WorkflowException($"This cannot be done while the invoice is \"{InvStatus.Label(inv.InvoiceStatus).ToLowerInvariant()}\".");
    }

    /// <summary>Who may hold, release, return or reject: the holder (with verifier), Accounts (with Accounts), the registry (Received).</summary>
    private bool MayHandle(ClaimsPrincipal u, InwardItem it, string invStatus) => invStatus switch
    {
        InvStatus.Received => IsOp(u, it),
        InvStatus.WithVerifier or InvStatus.Certified => it.HolderUserId == u.UserId(),
        InvStatus.SentToAccounts or InvStatus.PartPaid => access.IsAccounts(u),
        _ => false,
    };

    private static long PaymentCount(SqliteConnection c, SqliteTransaction tx, long id) =>
        Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM InvoicePayments WHERE ItemId = @id", new { id });

    /// <summary>Sets the invoice status and the matching item status (§3.3) in one step; closing statuses close the item.</summary>
    private static void SetStatus(SqliteConnection c, SqliteTransaction tx, InwardItem it, long rowVersion, string status, string? reason, ClaimsPrincipal u,
        string? itemSet = null, Dictionary<string, object?>? args = null)
    {
        args ??= new();
        var closure = status switch
        {
            InvStatus.Paid => Closure.InvoiceSettled, InvStatus.ClosedShort => Closure.InvoiceClosedShort, InvStatus.ReturnedToVendor => Closure.InvoiceReturned,
            InvStatus.Rejected => Closure.InvoiceRejected, InvStatus.Cancelled => Closure.Cancelled, _ => null,
        };
        var set = new List<string>();
        if (status != InvStatus.OnHold) { set.Add("Status = @ist"); args["ist"] = InvStatus.ItemStatusFor(status); }
        if (closure != null)
        {
            set.Add("ClosureType = @closure, ClosureRemarks = @reason, ClosedAt = @now, ClosedByName = @by, LastHolderUserId = COALESCE(HolderUserId, LastHolderUserId)");
            args["closure"] = closure; args["reason"] = reason; args["by"] = u.DisplayName();
        }
        if (itemSet != null) set.Add(itemSet);
        Bump(c, tx, it, rowVersion, set.Count == 0 ? "Id = Id" : string.Join(", ", set), args);
        Db.Exec(c, tx, $"""
            UPDATE InvoiceDetails SET InvoiceStatus = @status, StatusReason = COALESCE(@reason, StatusReason)
                {(closure is Closure.InvoiceSettled or Closure.InvoiceClosedShort ? ", SettledAt = @now" : "")}
            WHERE ItemId = @Id
            """, new { status, reason, now = Db.Now(), it.Id });
    }

    private NotificationMessage InvMsg(InwardItem it, string type, string what) => Msg(it, type, what);

    public void Certify(long id, string? certifiedOn, long rowVersion, ClaimsPrincipal u)
    {
        var on = Date("Certification date", certifiedOn, true)!;
        var it = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            RequireStatus(inv, InvStatus.WithVerifier);
            if (it.HolderUserId != u.UserId()) throw new WorkflowException("Only the verifying officer holding the invoice can certify it.");
            if (it.InwardedByUserId == u.UserId()) throw new WorkflowException("You inwarded this invoice, so another officer must certify it.");
            if (string.CompareOrdinal(on, inv.InvoiceDate) < 0 || string.CompareOrdinal(on, Today) > 0)
                throw new WorkflowException("The certification date must be between the invoice date and today.");
            SetStatus(c, tx, it, rowVersion, InvStatus.Certified, null, u, "AcceptedAt = COALESCE(AcceptedAt, @now)");
            Db.Exec(c, tx, "UPDATE InvoiceDetails SET CertifiedByUserId = @uid, CertifiedByName = @name, CertifiedOn = @on WHERE ItemId = @id",
                new { uid = u.UserId(), name = u.DisplayName(), on, id });
            Move(c, tx, id, u, "Certified", remarks: fmt.DateDot(on));
            return it;
        });
        Touch(u.UserId());
        Audit("Certified", it);
    }

    public void SendToAccounts(long id, long rowVersion, ClaimsPrincipal u)
    {
        var it = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            RequireStatus(inv, InvStatus.Certified);
            if (it.HolderUserId != u.UserId()) throw new WorkflowException("Only the officer holding the invoice can send it to Accounts.");
            SetStatus(c, tx, it, rowVersion, InvStatus.SentToAccounts, null, u, "HolderUserId = NULL, HolderSince = NULL, AcceptedAt = NULL, LastHolderUserId = HolderUserId");
            Db.Exec(c, tx, "UPDATE InvoiceDetails SET SentToAccountsAt = @now WHERE ItemId = @id", new { now = Db.Now(), id });
            Move(c, tx, id, u, "SentToAccounts");
            notifications.Notify(c, tx, access.AccountsIds(), NotificationActor.From(u), InvMsg(it, "SentToAccounts", "certified invoice sent to Accounts"));
            return it;
        });
        Touch(access.AccountsIds().Select(x => (long?)x).Append(u.UserId()).ToArray());
        Audit("SentToAccounts", it);
    }

    /// <summary>Accounts send an invoice back to the certifying officer (no payments yet); Received if that officer is inactive.</summary>
    public void SendBack(long id, string? remarks, long rowVersion, ClaimsPrincipal u)
    {
        RequireAccounts(u);
        var rem = T("Remarks", remarks, 1000, true);
        var (it, to) = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            RequireStatus(inv, InvStatus.SentToAccounts);
            if (PaymentCount(c, tx, id) > 0) throw new WorkflowException("Payments are recorded on this invoice, so it cannot be sent back.");
            long? to = inv.CertifiedByUserId is long cb && Db.Scalar<long>(c, tx, "SELECT IsActive FROM Users WHERE Id = @cb", new { cb }) == 1 && cb != it.InwardedByUserId ? cb : null;
            SetStatus(c, tx, it, rowVersion, to == null ? InvStatus.Received : InvStatus.WithVerifier, rem, u,
                "HolderUserId = @to, HolderSince = CASE WHEN @to IS NULL THEN NULL ELSE @now END, AcceptedAt = NULL, LastHolderUserId = COALESCE(@to, LastHolderUserId)", new() { ["to"] = to });
            Db.Exec(c, tx, "UPDATE InvoiceDetails SET CertifiedByUserId = NULL, CertifiedByName = NULL, CertifiedOn = NULL, SentToAccountsAt = NULL WHERE ItemId = @id", new { id });
            if (to is long t) AddParticipant(c, tx, id, t);
            Move(c, tx, id, u, "SentBack", to, to is long t2 ? UserName(c, tx, t2) : null, rem);
            var n = to is long t3 ? new List<long> { t3 } : access.OperatorIds(it.OfficeId).ToList();
            notifications.Notify(c, tx, n, NotificationActor.From(u), InvMsg(it, "SentBack", "sent back by Accounts"));
            return (it, to);
        });
        Touch(to, u.UserId());
        Audit("SentBack", it);
    }

    private sealed class Sums { public long Settled { get; set; } public long Adjust { get; set; } }

    private static Sums Totals(SqliteConnection c, SqliteTransaction tx, long id) => Db.Query<Sums>(c, tx, """
        SELECT COALESCE((SELECT SUM(NetPaise + TdsPaise + OtherDeductionsPaise) FROM InvoicePayments WHERE ItemId = @id), 0) AS Settled,
               COALESCE((SELECT SUM(CASE WHEN Kind = 'DebitNote' THEN AmountPaise ELSE -AmountPaise END) FROM InvoiceAdjustments WHERE ItemId = @id), 0) AS Adjust
        """, new { id }).First();

    /// <summary>A validated payment row, ready to insert (shared by the form and the ERP import).</summary>
    internal sealed record PaymentRow(string PaidOn, long Net, long Tds, long Other, string Voucher, string? Remarks);

    internal PaymentRow ValidatePayment(PaymentInput p, InvoiceDetail inv)
    {
        var on = Date("Paid on", p.PaidOn, true)!;
        if (string.CompareOrdinal(on, inv.InvoiceDate) < 0 || string.CompareOrdinal(on, Today) > 0)
            throw new WorkflowException("The payment date must be between the invoice date and today.");
        var net = Paise.Parse(p.Net, "Net amount paid") ?? 0;
        var tds = Paise.Parse(p.Tds, "TDS") ?? 0;
        var oth = Paise.Parse(p.OtherDeductions, "Other deductions") ?? 0;
        if (net < 0 || tds < 0 || oth < 0 || net + tds + oth <= 0) throw new WorkflowException("Enter the amount paid (net, TDS and other deductions cannot be negative).");
        var v = T("Voucher / UTR reference", p.VoucherRef, 60, true)!;
        return new PaymentRow(on, net, tds, oth, v, T("Remarks", p.Remarks, 500));
    }

    /// <summary>
    /// Records one payment inside the caller's transaction (§3.4): voucher not used by an unreversed payment on this item
    /// (R2-3); status recomputed from the sums; overpayment refused. Returns the new invoice status.
    /// </summary>
    internal string PaymentCore(SqliteConnection c, SqliteTransaction tx, InwardItem it, InvoiceDetail inv, PaymentRow p, bool checkedNotDuplicate, string source, ClaimsPrincipal u)
    {
        RequireStatus(inv, InvStatus.SentToAccounts, InvStatus.PartPaid);
        if (Db.Scalar<long>(c, tx, """
                SELECT COUNT(*) FROM InvoicePayments p WHERE p.ItemId = @id AND p.VoucherRef = @v COLLATE NOCASE AND p.ReversesId IS NULL
                  AND NOT EXISTS (SELECT 1 FROM InvoicePayments r WHERE r.ReversesId = p.Id)
                """, new { id = it.Id, v = p.Voucher }) > 0)
            throw new WorkflowException($"Voucher {p.Voucher} is already recorded on this invoice.");
        if (inv.OverrideReason != null && inv.DuplicateCheckedBy == null)
        {
            if (!checkedNotDuplicate) throw new WorkflowException("This invoice was saved as \"not a duplicate\". Tick \"Checked: not a duplicate payment\" before recording its first payment.");
            Db.Exec(c, tx, "UPDATE InvoiceDetails SET DuplicateCheckedBy = @by WHERE ItemId = @Id", new { by = u.DisplayName() + " on " + Today, it.Id });
        }
        Db.Exec(c, tx, """
            INSERT INTO InvoicePayments (ItemId, PaidOn, NetPaise, TdsPaise, OtherDeductionsPaise, VoucherRef, Remarks, Source, EnteredByUserId, EnteredByName, EnteredAt)
            VALUES (@Id, @PaidOn, @Net, @Tds, @Other, @Voucher, @Remarks, @source, @uid, @name, @now)
            """, new { it.Id, p.PaidOn, p.Net, p.Tds, p.Other, p.Voucher, p.Remarks, source, uid = u.UserId(), name = u.DisplayName(), now = Db.Now() });
        var s = Totals(c, tx, it.Id);
        var status = InvoiceRules.StatusFor(inv.TotalPaise + s.Adjust, s.Settled);
        Db.Exec(c, tx, "UPDATE InvoiceDetails SET SettledPaise = @Settled WHERE ItemId = @Id", new { s.Settled, it.Id });
        SetStatus(c, tx, it, it.RowVersion, status, null, u);
        Move(c, tx, it.Id, u, "Payment", remarks: $"{Paise.Format(p.Net + p.Tds + p.Other)} · {p.Voucher}" + (source == "Import" ? " (ERP import)" : ""));
        if (status == InvStatus.Paid && inv.CertifiedByUserId is long cb)
            notifications.Notify(c, tx, new[] { cb }, NotificationActor.From(u), InvMsg(it, "Paid", "invoice paid"));
        return status;
    }

    public void RecordPayment(long id, PaymentInput p, long rowVersion, ClaimsPrincipal u)
    {
        RequireAccounts(u);
        var (it, status) = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            if (it.RowVersion != rowVersion) throw new WorkflowException(Changed);
            var row = ValidatePayment(p, inv);
            return (it, PaymentCore(c, tx, it, inv, row, p.CheckedNotDuplicate, "Manual", u));
        });
        Touch(u.UserId());
        Audit("Payment", it, InvStatus.Label(status));
    }

    public void RecordAdjustment(long id, AdjustmentInput a, long rowVersion, ClaimsPrincipal u)
    {
        RequireAccounts(u);
        var kind = a.Kind is "CreditNote" or "DebitNote" ? a.Kind : throw new WorkflowException("Choose credit note or debit note.");
        var docNo = T("Document number", a.DocNo, 50, true)!;
        var docDate = Date("Document date", a.DocDate, true)!;
        if (string.CompareOrdinal(docDate, Today) > 0) throw new WorkflowException("The document date cannot be in the future.");
        var amount = Paise.Parse(a.Amount, "Amount") ?? throw new WorkflowException("Enter the amount.");
        if (amount <= 0) throw new WorkflowException("The amount must be more than zero.");
        var rem = T("Remarks", a.Remarks, 500);
        var (it, status, noteId) = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            if (it.RowVersion != rowVersion) throw new WorkflowException(Changed);
            RequireStatus(inv, InvStatus.SentToAccounts, InvStatus.PartPaid);
            InwardItem? note = null;
            if (!string.IsNullOrWhiteSpace(a.NoteNumber))
            {
                note = Db.Query<InwardItem>(c, tx, LoadSql + " WHERE i.Number = @n COLLATE NOCASE", new { n = a.NoteNumber.Trim() }).FirstOrDefault();
                if (note == null || note.ItemType != InwardType.CreditDebitNote || note.LinkedToId != id || !access.CanView(note, u))
                    throw new WorkflowException($"{a.NoteNumber.Trim()} is not an inwarded credit / debit note against this invoice.");
                if (!note.IsOpen) throw new WorkflowException($"{note.Number} is already closed: its adjustment has been recorded.");
            }
            if (Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM InvoiceAdjustments WHERE ItemId = @id AND Kind = @kind AND DocNo = @docNo COLLATE NOCASE", new { id, kind, docNo }) > 0)
                throw new WorkflowException($"{(kind == "CreditNote" ? "Credit" : "Debit")} note {docNo} is already recorded on this invoice.");
            var before = Totals(c, tx, id);
            if (kind == "CreditNote" && amount > inv.TotalPaise + before.Adjust - before.Settled)
                throw new WorkflowException("A credit note cannot be more than the amount outstanding.");
            Db.Exec(c, tx, """
                INSERT INTO InvoiceAdjustments (ItemId, Kind, DocNo, DocDate, AmountPaise, Remarks, EnteredByUserId, EnteredByName, EnteredAt)
                VALUES (@id, @kind, @docNo, @docDate, @amount, @rem, @uid, @name, @now)
                """, new { id, kind, docNo, docDate, amount, rem, uid = u.UserId(), name = u.DisplayName(), now = Db.Now() });
            var s = Totals(c, tx, id);
            var status = InvoiceRules.StatusFor(inv.TotalPaise + s.Adjust, s.Settled);         // R2-11: a credit note can settle it
            Db.Exec(c, tx, "UPDATE InvoiceDetails SET AdjustPaise = @Adjust WHERE ItemId = @id", new { s.Adjust, id });
            SetStatus(c, tx, it, it.RowVersion, status, null, u);
            Move(c, tx, id, u, "Adjustment", remarks: $"{(kind == "CreditNote" ? "Credit" : "Debit")} note {docNo}: {Paise.Format(amount)}");
            if (note is { IsOpen: true })
            {
                Bump(c, tx, note, note.RowVersion, "Status = 'Closed', ClosureType = 'ActionCompleted', ClosureRemarks = @r, ClosedAt = @now, ClosedByName = @by, LastHolderUserId = COALESCE(HolderUserId, LastHolderUserId)",
                    new() { ["r"] = $"Recorded against {it.Number}", ["by"] = u.DisplayName() });
                Move(c, tx, note.Id, u, "Closed", remarks: $"Recorded against {it.Number}");
            }
            return (it, status, note?.Id);
        });
        Touch(u.UserId());
        Audit("Adjustment", it, $"{kind}; {InvStatus.Label(status)}" + (noteId != null ? $"; note item #{noteId} closed" : ""));
    }

    public void CloseShort(long id, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        RequireAccounts(u);
        var rem = T("Reason", reason, 1000, true);
        var (it, n) = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            RequireStatus(inv, InvStatus.SentToAccounts, InvStatus.PartPaid);
            SetStatus(c, tx, it, rowVersion, InvStatus.ClosedShort, rem, u);
            Move(c, tx, id, u, "ClosedShort", remarks: rem);
            var n = access.ViewerIds().Concat(inv.CertifiedByUserId is long cb ? new[] { cb } : Array.Empty<long>()).Distinct().ToList();
            notifications.Notify(c, tx, n, NotificationActor.From(u), InvMsg(it, "ClosedShort", "invoice closed short"));
            return (it, n);
        });
        Touch(n.Select(x => (long?)x).Append(u.UserId()).ToArray());
        Audit("ClosedShort", it);
    }

    /// <summary>
    /// Reverses one payment (§3.3; R2-20): a negative row equal to minus the original. ClosedShort is undone, OnHold stays on
    /// hold with its "from" status recomputed, a closed invoice reopens with Accounts. Reversals cannot be reversed.
    /// </summary>
    public void Reverse(long id, long paymentId, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        RequireAccounts(u);
        var rem = T("Reason", reason, 500, true);
        var (it, status) = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            if (it.RowVersion != rowVersion) throw new WorkflowException(Changed);
            var p = Db.Query<InvoicePayment>(c, tx, "SELECT p.*, EXISTS (SELECT 1 FROM InvoicePayments r WHERE r.ReversesId = p.Id) AS Reversed FROM InvoicePayments p WHERE p.Id = @paymentId AND p.ItemId = @id",
                new { paymentId, id }).FirstOrDefault() ?? throw new WorkflowException("Payment not found.");
            if (p.ReversesId != null) throw new WorkflowException("A reversal cannot itself be reversed; record the payment again instead.");
            if (p.Reversed) throw new WorkflowException("This payment is already reversed.");
            Db.Exec(c, tx, """
                INSERT INTO InvoicePayments (ItemId, PaidOn, NetPaise, TdsPaise, OtherDeductionsPaise, VoucherRef, ReversesId, Remarks, Source, EnteredByUserId, EnteredByName, EnteredAt)
                VALUES (@id, @today, -@n, -@t, -@o, @v, @pid, @rem, 'Reversal', @uid, @name, @now)
                """, new { id, today = Today, n = p.NetPaise, t = p.TdsPaise, o = p.OtherDeductionsPaise, v = p.VoucherRef, pid = p.Id, rem, uid = u.UserId(), name = u.DisplayName(), now = Db.Now() });
            var s = Totals(c, tx, id);
            var computed = InvoiceRules.StatusFor(inv.TotalPaise + s.Adjust, s.Settled);
            Db.Exec(c, tx, "UPDATE InvoiceDetails SET SettledPaise = @Settled, SettledAt = NULL WHERE ItemId = @id", new { s.Settled, id });
            string status;
            if (inv.InvoiceStatus == InvStatus.OnHold)
            {
                status = InvStatus.OnHold;
                Db.Exec(c, tx, "UPDATE InvoiceDetails SET HoldFromStatus = @computed WHERE ItemId = @id", new { computed, id });
                Bump(c, tx, it, rowVersion, "Status = 'WithAccounts'", new());
            }
            else
            {
                status = computed;
                SetStatus(c, tx, it, rowVersion, status, null, u,
                    "ClosureType = NULL, ClosureRemarks = NULL, ClosedAt = NULL, ClosedByName = NULL, HolderUserId = NULL");
            }
            Move(c, tx, id, u, "Reversed", remarks: $"{Paise.Format(p.Settled)} · {p.VoucherRef}: {rem}");
            return (it, status);
        });
        Touch(u.UserId());
        Audit("PaymentReversed", it, InvStatus.Label(status));
    }

    public void Hold(long id, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        var rem = T("Reason", reason, 1000, true);
        var (it, n) = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            RequireStatus(inv, InvStatus.Received, InvStatus.WithVerifier, InvStatus.Certified, InvStatus.SentToAccounts, InvStatus.PartPaid);
            if (!MayHandle(u, it, inv.InvoiceStatus)) throw new WorkflowException("You cannot put this invoice on hold at its current stage.");
            SetStatus(c, tx, it, rowVersion, InvStatus.OnHold, rem, u);
            Db.Exec(c, tx, "UPDATE InvoiceDetails SET HoldFromStatus = @from WHERE ItemId = @id", new { from = inv.InvoiceStatus, id });
            Move(c, tx, id, u, "Held", remarks: rem);
            var n = HandlerIds(it);
            notifications.Notify(c, tx, n, NotificationActor.From(u), InvMsg(it, "Hold", "invoice put on hold"));
            return (it, n);
        });
        Touch(n.Select(x => (long?)x).Append(u.UserId()).ToArray());
        Audit("Held", it);
    }

    private List<long> HandlerIds(InwardItem it) => it.Status switch
    {
        ItemStatus.WithHolder when it.HolderUserId is long h => new List<long> { h },
        ItemStatus.WithAccounts => access.AccountsIds().ToList(),
        _ => access.OperatorIds(it.OfficeId).ToList(),
    };

    public void Release(long id, long rowVersion, ClaimsPrincipal u)
    {
        var (it, n) = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            RequireStatus(inv, InvStatus.OnHold);
            var from = inv.HoldFromStatus ?? InvStatus.Received;
            if (!MayHandle(u, it, from)) throw new WorkflowException("You cannot release this invoice.");
            SetStatus(c, tx, it, rowVersion, from, null, u);
            Db.Exec(c, tx, "UPDATE InvoiceDetails SET HoldFromStatus = NULL WHERE ItemId = @id", new { id });
            Move(c, tx, id, u, "Released");
            var n = HandlerIds(it);
            notifications.Notify(c, tx, n, NotificationActor.From(u), InvMsg(it, "Release", "invoice released from hold"));
            return (it, n);
        });
        Touch(n.Select(x => (long?)x).Append(u.UserId()).ToArray());
        Audit("Released", it);
    }

    public void ReturnOrReject(long id, bool reject, string? reason, long rowVersion, ClaimsPrincipal u)
    {
        var rem = T("Reason", reason, 1000, true);
        var (it, n) = InTx(db, (c, tx) =>
        {
            var (it, inv) = LoadInvoice(c, tx, id, u);
            RequireStatus(inv, InvStatus.Received, InvStatus.WithVerifier, InvStatus.Certified, InvStatus.SentToAccounts);
            if (!MayHandle(u, it, inv.InvoiceStatus)) throw new WorkflowException("You cannot return or reject this invoice at its current stage.");
            if (PaymentCount(c, tx, id) > 0) throw new WorkflowException("Payments are recorded on this invoice, so it cannot be returned or rejected.");
            SetStatus(c, tx, it, rowVersion, reject ? InvStatus.Rejected : InvStatus.ReturnedToVendor, rem, u);
            Move(c, tx, id, u, reject ? "Rejected" : "ReturnedToVendor", remarks: rem);
            var n = new List<long> { it.InwardedByUserId };
            if (inv.CertifiedByUserId is long cb) n.Add(cb);
            if (it.HolderUserId is long h) n.Add(h);
            notifications.Notify(c, tx, n.Distinct(), NotificationActor.From(u), InvMsg(it, reject ? "Rejected" : "Returned", reject ? "invoice rejected" : "invoice returned to the vendor"));
            return (it, n);
        });
        Touch(n.Select(x => (long?)x).Append(u.UserId()).ToArray());
        Audit(reject ? "Rejected" : "ReturnedToVendor", it);
    }
}
