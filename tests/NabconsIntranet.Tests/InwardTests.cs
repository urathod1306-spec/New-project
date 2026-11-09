using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Tests;

/// <summary>Inward register and invoices (release 1.7.0), test plan of DD-08 §13 and the round-2 items (§15).</summary>
public class InwardTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();
    private InwardWorkflow W => _app.Get<InwardWorkflow>();
    private InwardRepository R => _app.Get<InwardRepository>();
    private Db Db => _app.Get<Db>();
    private ClaimsPrincipal P(string u) => _app.Principal(u);
    private long Uid(string u) => _app.User(u).Id;
    private string D(int days) => _app.Get<Fmt>().Today.AddDays(days).ToString("yyyy-MM-dd");
    private long Rv(long id) => Db.Scalar<long>("SELECT RowVersion FROM InwardItems WHERE Id = @id", new { id });
    private InwardItem I(long id, string as_ = "coo") => R.Item(id, P(as_))!;
    private InvoiceDetail Inv(long id) => R.Invoice(id)!;
    private long Ho => R.Offices().First(o => o.Name == "Mumbai (Head Office)").Id;

    private InwardInput Letter(string subject = "Test letter", string? markTo = null, bool confidential = false, string type = InwardType.Letter) => new()
    {
        OfficeId = Ho, ItemType = type, Mode = "Post", ReceivedOn = D(0), SenderName = "Sender", SenderOrg = "Org", Subject = subject,
        ActionRequired = InwardAction.Reply, KeptAt = "Rack 1", IsConfidential = confidential, MarkToUserId = markTo == null ? null : Uid(markTo),
    };

    private long Invoice(string no, string? markTo = "approver1", string total = "1,180.00", bool msme = false, int dateDaysAgo = 10, string by = "publisher", string? gstin = "27AAPFU0939F1ZV")
    {
        var i = Letter("Invoice " + no, markTo, type: InwardType.Invoice);
        i.Invoice = new InvoiceInput { VendorName = "Test Vendor Pvt Ltd", VendorGstin = gstin, IsMsme = msme, MsmeClass = msme ? "Small" : null, InvoiceNo = no, InvoiceDate = D(-dateDaysAgo), Total = total, TermsDays = 30 };
        return W.Inward(i, P(by), true);
    }

    /// <summary>Invoice certified by approver1 and sent to Accounts (approver2).</summary>
    private long AtAccounts(string no, string total = "1,180.00", bool msme = false)
    {
        var id = Invoice(no, total: total, msme: msme);
        W.Certify(id, D(0), Rv(id), P("approver1"));
        W.SendToAccounts(id, Rv(id), P("approver1"));
        return id;
    }

    private void Pay(long id, string net, string voucher) => W.RecordPayment(id, new PaymentInput { PaidOn = D(0), Net = net, VoucherRef = voucher }, Rv(id), P("approver2"));

    // ------------------------------------------------------------------ pure rules

    [Fact]
    public void Gstin_paise_and_duplicate_key_rules()
    {
        Assert.Null(Gstin.Problem("27AAPFU0939F1ZV"));
        Assert.NotNull(Gstin.Problem("27AAPFU0939F1ZX"));                  // wrong check character
        Assert.NotNull(Gstin.Problem("00AAPFU0939F1ZV"));                  // no such state
        Assert.Equal(12345678L, Paise.Parse("1,23,456.78", "x"));
        Assert.Null(Paise.Parse(" ", "x"));
        Assert.Throws<WorkflowException>(() => Paise.Parse("1.234", "x"));
        Assert.Throws<WorkflowException>(() => Paise.Parse("-5", "x"));
        Assert.Equal("₹ 1,23,456.78", Paise.Format(12345678));
        Assert.Equal("acme", InvoiceRules.NormVendor("ACME Pvt. Ltd."));
        Assert.Equal("acmeindia", InvoiceRules.NormVendor("Acme India Private Limited"));
        Assert.Equal("INV24250012", InvoiceRules.NormInvoiceNo("inv/24-25/0012"));
        Assert.Equal(InvoiceRules.DupKey(null, "Acme Ltd", "A-1", "2026-27"), InvoiceRules.DupKey(null, "ACME LIMITED", "a1", "2026-27"));
        Assert.Equal("2026-05-31", InvoiceRules.DueOn("2026-04-16", 90, true, 45));     // MSME: 45 days at most
        Assert.Equal("2026-07-15", InvoiceRules.DueOn("2026-04-16", 90, false, 45));
        Assert.Equal(InvStatus.Paid, InvoiceRules.StatusFor(100, 100));
        Assert.Equal(InvStatus.PartPaid, InvoiceRules.StatusFor(100, 1));
        Assert.Equal(InvStatus.SentToAccounts, InvoiceRules.StatusFor(100, 0));
        Assert.Throws<WorkflowException>(() => InvoiceRules.StatusFor(100, 101));
    }

    // ------------------------------------------------------------------ schema

    [Fact]
    public void Migration_8_protects_the_register_with_triggers()
    {
        var id = W.Inward(Letter(), P("publisher"), false);
        using var c = Db.Open();
        Assert.Throws<SqliteException>(() => Db.Exec(c, null, "DELETE FROM InwardItems WHERE Id = @id", new { id }));
        Assert.Throws<SqliteException>(() => Db.Exec(c, null, "UPDATE InwardItems SET Number = 'X' WHERE Id = @id", new { id }));
        Assert.Throws<SqliteException>(() => Db.Exec(c, null, "DELETE FROM InwardMovements WHERE ItemId = @id", new { id }));
        Assert.Throws<SqliteException>(() => Db.Exec(c, null, "UPDATE InvoicePayments SET NetPaise = 1"));
        Assert.Throws<SqliteException>(() => Db.Exec(c, null, "DELETE FROM InwardMemberHistory"));
        Assert.Equal(1, Db.Scalar<long>("SELECT COUNT(*) FROM SchemaInfo WHERE Version = 8"));
    }

    [Fact]
    public void Numbers_run_per_office_and_offices_without_a_code_use_GEN()
    {
        var a = I(W.Inward(Letter(), P("publisher"), false));
        var b = I(W.Inward(Letter(), P("publisher"), false));
        Assert.Matches(@"^NCS/IN/HO/\d{4}-\d{2}/\d{5}$", a.Number);
        Assert.Equal(a.Serial + 1, b.Serial);
        var pune = R.Offices().First(o => o.Name == "Pune");
        W.Grant(Uid("bdu"), InwardRole.Operator, pune.Id, P("admin"));
        var gi = Letter(); gi.OfficeId = pune.Id;
        var g = I(W.Inward(gi, P("bdu"), false));
        Assert.Contains("/GEN/", g.Number);
        Assert.Equal(1, g.Serial);
    }

    // ------------------------------------------------------------------ letters

    [Fact]
    public void Letter_lifecycle_mark_accept_return_close_reopen()
    {
        var id = W.Inward(Letter(), P("publisher"), false);
        Assert.Equal(ItemStatus.Unassigned, I(id).Status);
        Assert.Throws<WorkflowException>(() => W.Mark(id, Uid("staff"), null, Rv(id), P("bdu")));        // not the registry
        W.Mark(id, Uid("staff"), null, Rv(id), P("publisher"));
        Assert.Equal(Uid("staff"), I(id).HolderUserId);
        Assert.Equal(D(7), I(id).ReplyDueOn);                                                           // Normal: 7 calendar days (D7)
        W.Accept(id, Rv(id), P("staff"));
        W.ReturnToRegistry(id, "Not my subject", Rv(id), P("staff"));
        Assert.Equal(ItemStatus.Unassigned, I(id).Status);
        Assert.False(_app.Get<InwardAccess>().CanView(I(id), P("staff")));                              // no longer a participant
        Assert.False(_app.Get<InwardAccess>().IsParticipant(id, Uid("staff")));
        W.Mark(id, Uid("approver1"), null, Rv(id), P("coo"));                                             // an Inward Viewer may mark
        Assert.Throws<WorkflowException>(() => W.Close(id, new CloseInput { ClosureType = Closure.Filed, Remarks = "x" }, Rv(id), P("approver1")));  // file no. needed
        W.Close(id, new CloseInput { ClosureType = Closure.Filed, FiledFileNo = "F/1", Remarks = "Filed" }, Rv(id), P("approver1"));
        Assert.Equal(ItemStatus.Closed, I(id).Status);
        Assert.Throws<WorkflowException>(() => W.Reopen(id, "x", Rv(id), P("staff")));
        W.Reopen(id, "More action needed", Rv(id), P("approver1"));
        Assert.Equal(Uid("approver1"), I(id).HolderUserId);
        Assert.Null(I(id).ClosureType);
        Assert.Throws<WorkflowException>(() => W.Accept(id, Rv(id) - 1, P("approver1")));                // stale row version
    }

    [Fact]
    public void Information_items_have_no_reply_due_and_reminders_reopen_closed_letters()
    {
        var info = Letter(); info.ActionRequired = InwardAction.Information; info.ReplyDueOn = D(3);
        Assert.Null(I(W.Inward(info, P("publisher"), false)).ReplyDueOn);
        var id = W.Inward(Letter(markTo: "staff"), P("publisher"), false);
        W.Close(id, new CloseInput { ClosureType = Closure.Replied, ReplyDate = D(0), Remarks = "Replied" }, Rv(id), P("staff"));
        var rem = Letter("Reminder"); rem.LinkedNumber = I(id).Number.ToLowerInvariant();
        var rid = W.Inward(rem, P("publisher"), false);
        Assert.Equal(Closure.Reminder, I(rid).ClosureType);
        Assert.Equal(ItemStatus.WithHolder, I(id).Status);                                               // reopened to the last holder
        Assert.Equal(1, I(id).ReminderCount);
        var bad = Letter("Reminder of nothing"); bad.LinkedNumber = "NCS/IN/HO/2099-00/99999";
        Assert.Throws<WorkflowException>(() => W.Inward(bad, P("publisher"), false));
        Assert.Throws<WorkflowException>(() => W.Reopen(rid, "x", Rv(rid), P("publisher")));            // a reminder never reopens
    }

    [Fact]
    public void Confidential_items_are_masked_and_kept_out_of_the_search_index()
    {
        var id = W.Inward(Letter("Disciplinary proceedings zebra", "approver1", confidential: true), P("publisher"), false);
        W.Grant(Uid("bdu"), InwardRole.Operator, Ho, P("admin"));
        var asOp = R.Item(id, P("bdu"))!;
        Assert.True(asOp.Masked);
        Assert.Equal("Confidential", asOp.ShownSubject);
        Assert.False(I(id).Masked);                                                                       // the Inward Viewer has full access
        Assert.False(_app.Get<InwardAccess>().CanView(I(id), P("staff")));                               // anyone else: not visible
        Assert.Equal(0, R.Register(P("coo"), new InwardFilter { Status = "all", Q = "zebra" }).Total);   // subject not indexed
        Assert.Equal(1, R.Register(P("coo"), new InwardFilter { Status = "all", Q = I(id).Number.Split('/').Last() }).Total);
        Assert.Throws<WorkflowException>(() => W.AddNote(id, "note", P("bdu")));                          // masked operator: no notes
        Assert.Throws<WorkflowException>(() => W.CopyTo(id, new() { Uid("staff") }, false, P("approver1")));   // needs the confirmation
        W.CopyTo(id, new() { Uid("staff") }, true, P("approver1"));
        Assert.False(R.Item(id, P("staff"))!.Masked);
    }

    [Fact]
    public void Self_inward_goes_to_the_user_and_an_invoice_never_to_its_inwarder()
    {
        var id = W.Inward(Letter(), P("bdu"), false);
        Assert.True(I(id).SelfInward);
        Assert.Equal(Uid("bdu"), I(id).HolderUserId);
        var inv = Invoice("SELF-1", markTo: null, by: "bdu");
        Assert.Equal(InvStatus.Received, Inv(inv).InvoiceStatus);
        Assert.Throws<WorkflowException>(() => Invoice("SELF-2", markTo: "bdu", by: "bdu"));
    }

    // ------------------------------------------------------------------ invoices

    [Fact]
    public void Duplicate_invoices_are_blocked_unless_overridden_and_the_first_payment_is_checked()
    {
        var a = Invoice("DUP/001");
        var ex = Assert.Throws<InwardConfirmException>(() => Invoice("dup-001"));
        Assert.True(ex.Blocked);
        Assert.Contains(I(a).Number, ex.Warnings[0]);
        var i = Letter("again", "approver1", type: InwardType.Invoice);
        i.Invoice = new InvoiceInput { VendorName = "Test Vendor", VendorGstin = "27AAPFU0939F1ZV", InvoiceNo = "DUP001", InvoiceDate = D(-10), Total = "1,180", NotDuplicate = true, OverrideReason = "Different job" };
        var b = W.Inward(i, P("publisher"), true);
        Assert.Equal("Different job", Inv(b).OverrideReason);
        Assert.DoesNotContain("Different job", Db.Scalar<string>("SELECT GROUP_CONCAT(Details) FROM AuditLog") ?? "");
        W.Certify(b, D(0), Rv(b), P("approver1"));
        W.SendToAccounts(b, Rv(b), P("approver1"));
        Assert.Throws<WorkflowException>(() => Pay(b, "100", "V1"));
        W.RecordPayment(b, new PaymentInput { PaidOn = D(0), Net = "100", VoucherRef = "V1", CheckedNotDuplicate = true }, Rv(b), P("approver2"));
        Assert.NotNull(Inv(b).DuplicateCheckedBy);
        // a warning (another FY) asks for confirmation
        var w = Letter("other FY", "approver1", type: InwardType.Invoice);
        w.Invoice = new InvoiceInput { VendorName = "Test Vendor", VendorGstin = "27AAPFU0939F1ZV", InvoiceNo = "DUP/001", InvoiceDate = D(-400), Total = "5" };
        Assert.False(Assert.Throws<InwardConfirmException>(() => W.Inward(w, P("publisher"), false)).Blocked);
    }

    [Fact]
    public void Segregation_certify_send_pay_reverse_and_reenter_the_voucher()
    {
        var own = Invoice("SEG-1", markTo: null);
        Assert.Throws<WorkflowException>(() => W.Mark(own, Uid("publisher"), null, Rv(own), P("coo")));   // never to the inwarder
        var id = Invoice("SEG-2", total: "1,000");
        Assert.Throws<WorkflowException>(() => W.Certify(id, D(1), Rv(id), P("approver1")));             // future date
        W.Certify(id, D(0), Rv(id), P("approver1"));
        W.SendToAccounts(id, Rv(id), P("approver1"));
        Assert.Equal(ItemStatus.WithAccounts, I(id).Status);
        Assert.Null(I(id).HolderUserId);
        Assert.Throws<WorkflowException>(() => W.SendBack(id, "x", Rv(id), P("approver1")));            // only Accounts send back
        W.SendBack(id, "Wrong rate", Rv(id), P("approver2"));
        Assert.Equal(InvStatus.WithVerifier, Inv(id).InvoiceStatus);
        Assert.Equal(Uid("approver1"), I(id).HolderUserId);
        Assert.Null(Inv(id).CertifiedByUserId);
    }

    [Fact]
    public void Payments_part_full_overpay_reversal_and_voucher_reuse()
    {
        var id = AtAccounts("PAY-1", "1,000");
        Assert.Throws<WorkflowException>(() => W.RecordPayment(id, new PaymentInput { PaidOn = D(0), Net = "100", VoucherRef = "A" }, Rv(id), P("staff")));   // not Accounts
        Pay(id, "400", "UTR-1");
        Assert.Equal(InvStatus.PartPaid, Inv(id).InvoiceStatus);
        Assert.Throws<WorkflowException>(() => Pay(id, "700", "UTR-2"));                                 // overpay
        Assert.Throws<WorkflowException>(() => Pay(id, "100", "utr-1"));                                 // voucher already used
        W.RecordPayment(id, new PaymentInput { PaidOn = D(0), Net = "590", Tds = "10", VoucherRef = "UTR-2" }, Rv(id), P("approver2"));
        Assert.Equal(InvStatus.Paid, Inv(id).InvoiceStatus);
        Assert.Equal(ItemStatus.Closed, I(id).Status);
        var first = R.Payments(id).First();
        W.Reverse(id, first.Id, "Bounced", Rv(id), P("approver2"));
        Assert.Equal(InvStatus.PartPaid, Inv(id).InvoiceStatus);
        Assert.Equal(ItemStatus.WithAccounts, I(id).Status);
        Assert.Null(I(id).ClosureType);
        var reversal = R.Payments(id).Single(p => p.ReversesId != null);
        Assert.Throws<WorkflowException>(() => W.Reverse(id, reversal.Id, "x", Rv(id), P("approver2")));   // reversals are final
        Assert.Throws<WorkflowException>(() => W.Reverse(id, first.Id, "x", Rv(id), P("approver2")));      // already reversed
        Pay(id, "400", "UTR-1");                                                                          // R2-3: same voucher again after reversal
        Assert.Equal(InvStatus.Paid, Inv(id).InvoiceStatus);
        Assert.Throws<WorkflowException>(() => W.Cancel(id, "x", Rv(id), P("publisher")));
    }

    [Fact]
    public void Credit_note_can_settle_and_close_short_and_hold_rules()
    {
        var id = AtAccounts("CN-1", "1,000");
        Assert.Throws<WorkflowException>(() => W.RecordAdjustment(id, new AdjustmentInput { Kind = "CreditNote", DocNo = "C1", DocDate = D(0), Amount = "1,001" }, Rv(id), P("approver2")));
        W.RecordAdjustment(id, new AdjustmentInput { Kind = "CreditNote", DocNo = "C1", DocDate = D(0), Amount = "1,000" }, Rv(id), P("approver2"));
        Assert.Equal(InvStatus.Paid, Inv(id).InvoiceStatus);                                              // R2-11
        Assert.Equal(Closure.InvoiceSettled, I(id).ClosureType);

        var s = AtAccounts("CS-1", "1,000");
        W.Hold(s, "Query", Rv(s), P("approver2"));
        Assert.Throws<WorkflowException>(() => Pay(s, "1", "H"));
        W.Release(s, Rv(s), P("approver2"));
        Pay(s, "500", "U");
        W.CloseShort(s, "Balance waived", Rv(s), P("approver2"));
        Assert.Equal(InvStatus.ClosedShort, Inv(s).InvoiceStatus);
        Assert.True(Db.Scalar<long>("SELECT COUNT(*) FROM Notifications WHERE Type = 'Inward.ClosedShort' AND UserId = @u", new { u = Uid("coo") }) > 0);

        var r = Invoice("RET-1");
        W.ReturnOrReject(r, false, "Wrong GSTIN", Rv(r), P("approver1"));
        Assert.Equal(InvStatus.ReturnedToVendor, Inv(r).InvoiceStatus);
        Assert.Equal(ItemStatus.Closed, I(r).Status);
    }

    [Fact]
    public void Credit_note_items_are_against_an_invoice_and_closed_by_the_adjustment()
    {
        var inv = AtAccounts("CNI-1", "1,000");
        var bad = Letter("credit note", type: InwardType.CreditDebitNote);
        Assert.Throws<WorkflowException>(() => W.Inward(bad, P("publisher"), false));
        bad.LinkedNumber = I(inv).Number;
        var note = W.Inward(bad, P("publisher"), false);
        Assert.False(R.Item(note, P("approver2"))!.Masked);                                               // Accounts see notes too
        W.RecordAdjustment(inv, new AdjustmentInput { Kind = "CreditNote", DocNo = "C9", DocDate = D(0), Amount = "100", NoteNumber = I(note).Number }, Rv(inv), P("approver2"));
        Assert.Equal(ItemStatus.Closed, I(note).Status);
        Assert.Equal(90000, Inv(inv).EffectiveTotal);
    }

    [Fact]
    public void Msme_report_lists_micro_and_small_unpaid_or_paid_late()
    {
        var late = AtAccounts("MS-1", "1,000", msme: true);
        Db.Exec("UPDATE InvoiceDetails SET InvoiceDate = @d, PaymentDueOn = @due WHERE ItemId = @late", new { d = D(-60), due = D(-30), late });
        var paid = AtAccounts("MS-2", "1,000", msme: true);
        Pay(paid, "1,000", "M2");
        var trader = Invoice("MS-3", msme: true);
        Db.Exec("UPDATE InvoiceDetails SET IsTrader = 1 WHERE ItemId = @trader", new { trader });
        var rows = R.MsmeReport(P("coo"), _app.Get<Fmt>().Today, null);
        Assert.Contains(rows, r => r.ItemId == late && r.OutstandingAtCutoff == 100000);
        Assert.DoesNotContain(rows, r => r.ItemId == paid);                                              // paid on time
        Assert.DoesNotContain(rows, r => r.ItemId == trader);
        Assert.True(R.InvoiceDashboard(P("approver2")).Msme45 >= 1);
    }

    // ------------------------------------------------------------------ e-Note, transfer, grants

    [Fact]
    public void Convert_to_eNote_is_idempotent_without_files_and_cannot_be_reopened()
    {
        var id = W.Inward(Letter("Proposal", "staff"), P("publisher"), false);
        var cat = _app.Get<MasterRepository>().Names(ENoteMaster.Category).First();
        var note = W.ConvertToENote(id, cat, true, "Taken up on e-Note", Rv(id), P("staff"));
        Assert.Equal(Closure.ConvertedToENote, I(id).ClosureType);
        Assert.Equal(id, Db.Scalar<long>("SELECT SourceInwardId FROM ENotes WHERE Id = @note", new { note }));
        Assert.StartsWith("Inward ", Db.Scalar<string>("SELECT Subject FROM ENotes WHERE Id = @note", new { note }));
        Assert.Throws<WorkflowException>(() => W.Reopen(id, "x", Rv(id), P("staff")));
        using var c = Db.Open();
        Assert.Throws<SqliteException>(() => Db.Exec(c, null, "UPDATE ENotes SET SourceInwardId = @id WHERE Id <> @note AND Id = (SELECT MIN(Id) FROM ENotes WHERE Id <> @note)", new { id, note }));
        var form = new FormFile(new MemoryStream(new byte[] { 1 }), 0, 1, "f", "a.pdf");
        await_throws(() => _app.Get<ENoteWorkflow>().AttachAsync(note, new[] { (IFormFile)form }, null, P("staff")));
    }

    private static void await_throws(Func<Task> f) => Assert.ThrowsAsync<WorkflowException>(f).GetAwaiter().GetResult();

    [Fact]
    public void Transfer_skips_invoices_the_new_holder_inwarded_and_uncertifies()
    {
        var letter = W.Inward(Letter(markTo: "approver1"), P("publisher"), false);
        var cert = Invoice("TR-1");
        W.Certify(cert, D(0), Rv(cert), P("approver1"));
        var own = Invoice("TR-2", by: "staff", markTo: "approver1");                                       // staff self-inwarded
        var pv = W.PreviewTransfer(Uid("approver1"), Uid("staff"));
        Assert.Equal(1, pv.SkippedInvoices);
        Assert.Throws<WorkflowException>(() => W.Transfer(Uid("approver1"), Uid("staff"), null, P("staff")));
        W.Transfer(Uid("approver1"), Uid("staff"), "Transferred", P("coo"));
        Assert.Equal(Uid("staff"), I(letter).HolderUserId);
        Assert.Equal(InvStatus.WithVerifier, Inv(cert).InvoiceStatus);
        Assert.Null(Inv(cert).CertifiedByUserId);
        Assert.Equal(Uid("approver1"), I(own).HolderUserId);
    }

    [Fact]
    public void Only_the_administrator_grants_and_never_to_themselves()
    {
        Assert.Throws<WorkflowException>(() => W.Grant(Uid("bdu"), InwardRole.Viewer, 0, P("coo")));
        Assert.Throws<WorkflowException>(() => W.Grant(Uid("admin"), InwardRole.Viewer, 0, P("admin")));
        Assert.Throws<WorkflowException>(() => W.Grant(Uid("bdu"), InwardRole.Operator, 0, P("admin")));      // office needed
        W.Grant(Uid("bdu"), InwardRole.Viewer, 0, P("admin"));
        Assert.True(_app.Get<InwardAccess>().IsViewer(P("bdu")));
        Assert.Throws<WorkflowException>(() => W.Grant(Uid("bdu"), InwardRole.Viewer, 0, P("admin")));
        var m = R.Members().First(x => x.UserId == Uid("bdu"));
        W.Revoke(m.Id, P("admin"));
        Assert.False(_app.Get<InwardAccess>().IsViewer(P("bdu")));
        Assert.Equal(2, Db.Scalar<long>("SELECT COUNT(*) FROM InwardMemberHistory WHERE UserId = @u", new { u = Uid("bdu") }));
    }

    // ------------------------------------------------------------------ reminders, desk, imports

    [Fact]
    public void Daily_reminders_are_aggregated_once_and_markers_do_not_bump_row_versions()
    {
        var a = W.Inward(Letter(markTo: "approver1"), P("publisher"), false);
        Db.Exec("UPDATE InwardItems SET ReplyDueOn = @d WHERE Id = @a", new { d = D(-1), a });
        W.Inward(Letter(markTo: "approver1"), P("publisher"), false);
        var rv = Rv(a);
        var today = _app.Get<Fmt>().Today;
        long N() => Db.Scalar<long>("SELECT COUNT(*) FROM Notifications WHERE Type = 'Inward.Reminder' AND UserId = @u", new { u = Uid("approver1") });
        W.DailyReminders(today);
        Assert.Equal(1, N());
        Assert.Contains("overdue", Db.Scalar<string>("SELECT Title FROM Notifications WHERE Type = 'Inward.Reminder' AND UserId = @u", new { u = Uid("approver1") }));
        Assert.Equal(rv, Rv(a));
        W.DailyReminders(today);
        Assert.Equal(1, N());
        W.DailyReminders(today.AddDays(3));
        Assert.Equal(2, N());
    }

    [Fact]
    public void My_Desk_counts_holder_registry_and_accounts_queues()
    {
        var desk = _app.Get<DeskService>();
        W.Inward(Letter(markTo: "bdu"), P("publisher"), false);
        W.Inward(Letter(), P("publisher"), false);
        AtAccounts("DESK-1");
        Thread.Sleep(1100);
        Assert.True(desk.Counts(P("bdu")).InwardMine >= 1);
        Assert.True(desk.Counts(P("bdu")).InwardAssigned);
        Assert.True(desk.Counts(P("publisher")).InwardUnassigned >= 1);
        Assert.True(desk.Counts(P("approver2")).InvoiceQueue >= 1);
        Assert.Equal(0, desk.Counts(P("staff")).InvoiceQueue);
    }

    [Fact]
    public void Project_code_and_erp_payment_imports()
    {
        var csv = "Code,Name,OwnerUserName,Active\r\nNC-T-1,Test project,approver1,Yes\r\nNC-T-1,Duplicate,,Yes\r\n";
        var rows = W.ParseProjectCodes(csv, out var err);
        Assert.Null(err);
        Assert.Contains(rows, r => r.Errors.Count > 0);
        Assert.Throws<WorkflowException>(() => W.ImportProjectCodes(csv, false, P("approver2")));
        var good = "Code,Name,OwnerUserName,Active\r\nnc-t-1,Test project,approver1,Yes\r\n";
        Assert.Throws<WorkflowException>(() => W.ImportProjectCodes(good, false, P("staff")));
        Assert.Equal(1, W.ImportProjectCodes(good, false, P("approver2")));
        Assert.Throws<WorkflowException>(() => W.ImportProjectCodes(good, false, P("approver2")));      // same file twice

        var id = AtAccounts("ERP-1", "1,000");
        var number = I(id).Number;
        var pay = $"InwardNo,VendorGstin,VendorName,InvoiceNo,InvoiceDate,PaidOn,Amount (net),TDS,VoucherRef\r\n{number},,,,,{D(0)},400,0,E1\r\n" +
                  $",27AAPFU0939F1ZV,,ERP-1,{D(-10)},{D(0)},700,0,E2\r\n,,,NONE,{D(0)},{D(0)},1,0,E3\r\n{number},,,,,{D(0)},5,0,\r\n";
        var pv = W.PreviewPayments(pay, P("approver2"), out err);
        Assert.Null(err);
        Assert.Equal(new[] { PaymentCategory.New, PaymentCategory.Overpay, PaymentCategory.Unmatched, PaymentCategory.Invalid }, pv.Select(r => r.Category).ToArray());
        Assert.Equal(1, W.ImportPayments(pay, P("approver2")));
        Assert.Equal(InvStatus.PartPaid, Inv(id).InvoiceStatus);
        Assert.Equal("Import", R.Payments(id).Single().Source);
        Assert.Throws<WorkflowException>(() => W.ImportPayments(pay, P("approver2")));
        Assert.Equal(PaymentCategory.Recorded, W.PreviewPayments(pay + " ", P("approver2"), out _)[0].Category);
    }

    // ------------------------------------------------------------------ HTTP

    [Fact]
    public async Task Files_are_refused_except_on_the_import_pages()
    {
        var id = W.Inward(Letter(markTo: "staff"), P("publisher"), false);
        var c = await _app.SignIn("staff");
        var token = await TestApp.Token(c, $"/Inward/Item/{id}");
        var mp = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { new StringContent("x"), "text" } };
        mp.Add(new ByteArrayContent(new byte[] { 0x25, 0x50 }), "scan", "scan.pdf");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await c.PostAsync($"/Inward/Item/{id}?handler=Note", mp)).StatusCode);
        Assert.Contains("/Inward/Payments", UploadGuard.MultipartAllowed);
        Assert.Contains("/Inward/ProjectCodes", UploadGuard.MultipartAllowed);
    }

    [Fact]
    public async Task Pages_render_for_each_role()
    {
        var conf = W.Inward(Letter("secret", "approver1", confidential: true), P("publisher"), false);
        async Task<HttpStatusCode> Get(string user, string url) => (await (await _app.SignIn(user)).GetAsync(url)).StatusCode;
        foreach (var url in new[] { "/Inward", "/Inward/Register", "/Inward/New", "/Inward/Mine", "/Inward/Invoices", $"/Inward/Item/{conf}" })
            Assert.Equal(HttpStatusCode.OK, await Get("coo", url));
        Assert.Equal(HttpStatusCode.OK, await Get("approver2", "/Inward/Payments"));
        Assert.Equal(HttpStatusCode.OK, await Get("approver2", "/Inward/ProjectCodes"));
        Assert.Equal(HttpStatusCode.OK, await Get("admin", "/Inward/Access"));
        Assert.Equal(HttpStatusCode.OK, await Get("staff", "/Inward/Mine"));
        Assert.Equal(HttpStatusCode.OK, await Get("staff", "/Inward/New"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("staff", $"/Inward/Item/{conf}"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("staff", "/Inward/Payments"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("coo", "/Inward/Access"));
        Assert.Equal(HttpStatusCode.NotFound, await Get("staff", "/Inward/Invoices"));
    }

    [Fact]
    public async Task Module_can_be_switched_off()
    {
        using var off = new TestApp(("Inward__Enabled", "false"));
        var c = await off.SignIn("coo");
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/Inward")).StatusCode);
        Assert.DoesNotContain("href=\"/Inward", await (await c.GetAsync("/")).Content.ReadAsStringAsync());
    }
}

