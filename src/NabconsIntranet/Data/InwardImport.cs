using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

/// <summary>A project-code CSV row (DD-08 §6.8).</summary>
public sealed record ProjectCodeRow(int Line, string Code, string Name, string? OwnerUserName, long? OwnerUserId, bool Active, string Change, List<string> Errors);

/// <summary>An ERP payment CSV row and how it would be imported (DD-08 §6.11; R2-12).</summary>
public sealed class PaymentImportRow
{
    public int Line { get; set; }
    public string? InwardNo { get; set; }
    public string? InvoiceNo { get; set; }
    public string? Vendor { get; set; }
    public string? PaidOn { get; set; }
    public long Settled { get; set; }
    public string? Voucher { get; set; }
    public long? ItemId { get; set; }
    public string? MatchedNumber { get; set; }
    public string Category { get; set; } = "";
    public string? Problem { get; set; }
    internal PaymentInput Input { get; set; } = new();
}

public static class PaymentCategory
{
    public const string New = "Matched — new", Recorded = "Already recorded", Unmatched = "Unmatched", NotPayable = "Not payable now",
        Overpay = "Would overpay", Manual = "Overridden duplicate — enter manually", Invalid = "Invalid";
    public static readonly string[] All = { New, Recorded, Unmatched, NotPayable, Overpay, Manual, Invalid };
}

/// <summary>
/// CSV imports of the inward module, read in memory only (never stored): project codes and ERP payments. Both are
/// re-checked inside the write transaction and recorded in InwardImports. A project-code file is refused a second time;
/// a payment file may come again, and rows whose voucher is already recorded on the invoice are skipped.
/// </summary>
public sealed partial class InwardWorkflow
{
    public static readonly string[] ProjectColumns = { "Code", "Name", "OwnerUserName", "Active" };
    public static readonly string[] PaymentColumns = { "InwardNo", "VendorGstin", "VendorName", "InvoiceNo", "InvoiceDate", "PaidOn", "Amount", "TDS", "OtherDeductions", "VoucherRef", "Remarks" };

    public static byte[] ProjectTemplate() => Template(ProjectColumns, new[] { "NC-2026-014", "Watershed impact study, Maharashtra", "approver1", "Yes" });
    public static byte[] PaymentTemplate() => Template(PaymentColumns, new[]
        { "NCS/IN/HO/2026-27/00012", "27AAPFU0939F1ZV", "Sample Vendor Pvt Ltd", "INV/26-27/101", "05.10.2026", "28.10.2026", "1,06,200.00", "2,000.00", "0", "UTR123456789", "" });

    private static byte[] Template(string[] cols, string[] sample)
    {
        var sb = new StringBuilder(string.Join(",", cols)).Append("\r\n").Append(string.Join(",", sample.Select(Csv.Escape))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>Parses the CSV text into rows keyed by column name; returns null and a reason for a bad file.</summary>
    private List<(int line, Func<string, string?> v)>? Table(string text, string[] columns, string[] required, out string? error, Dictionary<string, string>? aliases = null)
    {
        error = null;
        if (Encoding.UTF8.GetByteCount(text) > o.MaxImportKB * 1024L) { error = $"The file is larger than {o.MaxImportKB:N0} KB."; return null; }
        var table = Csv.Parse(text);
        if (table.Count == 0) { error = "The file is empty."; return null; }
        var header = table[0].Select(h => h.Trim()).Select(h => aliases != null && aliases.TryGetValue(h, out var a) ? a : h).ToList();
        var unknown = header.Where(h => h.Length > 0 && !columns.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0) { error = "Unknown column(s): " + string.Join(", ", unknown) + ". Download the template for the column names."; return null; }
        var missing = required.Where(r => !header.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0) { error = "Missing column(s): " + string.Join(", ", missing) + "."; return null; }
        if (table.Count - 1 > o.MaxImportRows) { error = $"At most {o.MaxImportRows:N0} rows per file (this one has {table.Count - 1:N0})."; return null; }
        var idx = columns.ToDictionary(c => c, c => header.FindIndex(h => h.Equals(c, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        var rows = new List<(int, Func<string, string?>)>();
        for (var r = 1; r < table.Count; r++)
        {
            var cells = table[r];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add((r + 1, col => idx[col] >= 0 && idx[col] < cells.Count && !string.IsNullOrWhiteSpace(cells[idx[col]]) ? cells[idx[col]].Trim() : null));
        }
        if (rows.Count == 0) { error = "The file has no data rows."; return null; }
        return rows;
    }

    private void RequireNewFile(SqliteConnection? c, SqliteTransaction? tx, string kind, string sha)
    {
        var n = c == null ? db.Scalar<long>("SELECT COUNT(*) FROM InwardImports WHERE Kind = @kind AND Sha256 = @sha", new { kind, sha })
                          : Db.Scalar<long>(c, tx, "SELECT COUNT(*) FROM InwardImports WHERE Kind = @kind AND Sha256 = @sha", new { kind, sha });
        if (n > 0) throw new WorkflowException("This exact file has already been imported.");
    }

    // ================================================================= project codes

    private static readonly Regex CodeRx = new("^[A-Za-z0-9][A-Za-z0-9/_.-]{0,49}$", RegexOptions.Compiled);

    public List<ProjectCodeRow> ParseProjectCodes(string text, out string? error)
    {
        var table = Table(ComplianceImport.Normalise(text), ProjectColumns, new[] { "Code", "Name" }, out error);
        if (table == null) return new();
        var users = db.Query<PersonOption>("SELECT Id, DisplayName, Username FROM Users WHERE IsActive = 1")
            .GroupBy(x => x.Username, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var existing = db.Query<ProjectCode>("SELECT * FROM ProjectCodes").ToDictionary(p => p.Code, StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ProjectCodeRow>();
        foreach (var (line, v) in table)
        {
            var errors = new List<string>();
            var code = v("Code") ?? "";
            if (!CodeRx.IsMatch(code)) errors.Add("Code: 1–50 letters, digits and / _ . - only.");
            else if (!seen.Add(code)) errors.Add("This code appears twice in the file.");
            var name = "";
            try { name = T("Name", v("Name"), 200, true)!; } catch (WorkflowException ex) { errors.Add(ex.Message); }
            var owner = v("OwnerUserName");
            long? ownerId = null;
            if (owner != null) { if (users.TryGetValue(owner, out var p)) ownerId = p.Id; else errors.Add($"Owner {owner} is not an active sign-in name."); }
            var activeText = (v("Active") ?? "yes").ToLowerInvariant();
            bool active = activeText is "yes" or "y" or "true" or "1" or "active";
            if (!active && activeText is not ("no" or "n" or "false" or "0" or "inactive")) errors.Add("Active must be Yes or No.");
            var change = !existing.TryGetValue(code, out var old) ? "New"
                : old.Name == name && old.OwnerUserId == ownerId && old.IsActive == active ? "Unchanged" : "Updated";
            rows.Add(new ProjectCodeRow(line, code, name, owner, ownerId, active, change, errors));
        }
        return rows;
    }

    public int ImportProjectCodes(string text, bool deactivateMissing, ClaimsPrincipal u)
    {
        RequireAccounts(u);
        var norm = ComplianceImport.Normalise(text);
        var sha = ComplianceImport.Sha(norm);
        RequireNewFile(null, null, "ProjectCodes", sha);
        var rows = ParseProjectCodes(norm, out var error);
        if (error != null) throw new WorkflowException(error);
        if (rows.Any(r => r.Errors.Count > 0)) throw new WorkflowException("Some rows have errors; correct the file and upload it again.");
        var (changed, deactivated) = InTx(db, (c, tx) =>
        {
            RequireNewFile(c, tx, "ProjectCodes", sha);
            var now = Db.Now();
            var n = 0;
            foreach (var r in rows.Where(r => r.Change != "Unchanged"))
            {
                Db.Exec(c, tx, """
                    INSERT INTO ProjectCodes (Code, Name, OwnerUserId, IsActive, UpdatedAt, UpdatedBy) VALUES (@Code, @Name, @OwnerUserId, @Active, @now, @by)
                    ON CONFLICT (Code) DO UPDATE SET Name = excluded.Name, OwnerUserId = excluded.OwnerUserId, IsActive = excluded.IsActive, UpdatedAt = excluded.UpdatedAt, UpdatedBy = excluded.UpdatedBy
                    """, new { r.Code, r.Name, r.OwnerUserId, r.Active, now, by = u.DisplayName() });
                n++;
            }
            var d = 0;
            if (deactivateMissing)
            {
                var keep = rows.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var code in Db.Query<ProjectCode>(c, tx, "SELECT Code FROM ProjectCodes WHERE IsActive = 1").Select(p => p.Code).Where(x => !keep.Contains(x)))
                {
                    Db.Exec(c, tx, "UPDATE ProjectCodes SET IsActive = 0, UpdatedAt = @now, UpdatedBy = @by WHERE Code = @code", new { code, now, by = u.DisplayName() });
                    d++;
                }
            }
            Db.Exec(c, tx, "INSERT INTO InwardImports (Kind, Sha256, Rows, ByName, At) VALUES ('ProjectCodes', @sha, @n, @by, @now)", new { sha, n = rows.Count, by = u.DisplayName(), now });
            return (n, d);
        });
        audit.Log("Inward.ProjectCodesImported", "ProjectCodes", null, $"{rows.Count} row(s); {changed} added or changed; {deactivated} deactivated");
        return changed + deactivated;
    }

    // ================================================================= ERP payments

    private static readonly Dictionary<string, string> PaymentAliases = new(StringComparer.OrdinalIgnoreCase) { ["Amount (net)"] = "Amount", ["NetAmount"] = "Amount" };

    private sealed class Candidate
    {
        public long ItemId { get; set; }
        public string Number { get; set; } = "";
        public string ItemType { get; set; } = "";
    }

    /// <summary>Classifies every row (§6.11). With a connection, runs inside the import transaction against current data.</summary>
    private List<PaymentImportRow> ClassifyPayments(SqliteConnection c, SqliteTransaction? tx, string text, out string? error)
    {
        var table = Table(text, PaymentColumns, new[] { "PaidOn", "Amount", "VoucherRef" }, out error, PaymentAliases);
        if (table == null) return new();
        var pending = new Dictionary<long, long>();          // item → amount of earlier rows of this file (R2-12)
        var vouchers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<PaymentImportRow>();
        foreach (var (line, v) in table)
        {
            var r = new PaymentImportRow { Line = line, InwardNo = v("InwardNo"), InvoiceNo = v("InvoiceNo"), Vendor = v("VendorName") ?? v("VendorGstin"), PaidOn = v("PaidOn"), Voucher = v("VoucherRef") };
            r.Input = new PaymentInput { PaidOn = v("PaidOn"), Net = v("Amount"), Tds = v("TDS"), OtherDeductions = v("OtherDeductions"), VoucherRef = v("VoucherRef"), Remarks = v("Remarks") };
            rows.Add(r);
            try
            {
                // match: by inward number, else by the duplicate key; anything ambiguous is unmatched
                List<Candidate> found;
                if (r.InwardNo != null)
                    found = Db.Query<Candidate>(c, tx, "SELECT Id AS ItemId, Number, ItemType FROM InwardItems WHERE Number = @n COLLATE NOCASE", new { n = r.InwardNo });
                else
                {
                    var inv = v("InvoiceNo");
                    var date = Date("InvoiceDate", v("InvoiceDate"));
                    var g = Gstin.Normalise(v("VendorGstin"));
                    if (inv == null || date == null || (g == null && v("VendorName") == null)) { r.Category = PaymentCategory.Unmatched; r.Problem = "Give the inward number, or the vendor, invoice number and invoice date."; continue; }
                    var key = InvoiceRules.DupKey(g, v("VendorName") ?? "", inv, LeadRepository.FinancialYear(DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)));
                    found = Db.Query<Candidate>(c, tx, "SELECT i.Id AS ItemId, i.Number, i.ItemType FROM InvoiceDetails d JOIN InwardItems i ON i.Id = d.ItemId WHERE d.DupKey = @key AND d.InvoiceStatus <> 'Cancelled'", new { key });
                }
                if (found.Count != 1 || found[0].ItemType != InwardType.Invoice)
                { r.Category = PaymentCategory.Unmatched; r.Problem = found.Count > 1 ? "More than one invoice matches (overridden duplicates); enter it manually." : "No invoice matches."; continue; }
                r.ItemId = found[0].ItemId; r.MatchedNumber = found[0].Number;
                var detail = InvoiceOf(c, tx!, r.ItemId.Value)!;
                if (r.InwardNo != null)
                {
                    // matched by number: the row's own invoice details, when given, must agree (a mistyped number must not pay another vendor)
                    var g = Gstin.Normalise(v("VendorGstin"));
                    var no = v("InvoiceNo");
                    if ((no != null && InvoiceRules.NormInvoiceNo(no) != InvoiceRules.NormInvoiceNo(detail.InvoiceNo)) || (g != null && detail.VendorGstin != null && g != detail.VendorGstin))
                    { r.Category = PaymentCategory.Unmatched; r.Problem = $"{r.MatchedNumber} is invoice {detail.InvoiceNo}{(detail.VendorGstin != null ? " of " + detail.VendorGstin : "")}, which does not agree with this row."; r.ItemId = null; continue; }
                }
                var row = ValidatePayment(r.Input, detail);
                r.Settled = row.Net + row.Tds + row.Other;
                var used = Db.Scalar<long>(c, tx, """
                    SELECT COUNT(*) FROM InvoicePayments p WHERE p.ItemId = @id AND p.VoucherRef = @v COLLATE NOCASE AND p.ReversesId IS NULL
                      AND NOT EXISTS (SELECT 1 FROM InvoicePayments x WHERE x.ReversesId = p.Id)
                    """, new { id = r.ItemId, v = row.Voucher }) > 0;
                if (used || !vouchers.Add(r.ItemId + "|" + row.Voucher)) { r.Category = PaymentCategory.Recorded; continue; }
                if (detail.InvoiceStatus is not (InvStatus.SentToAccounts or InvStatus.PartPaid))
                { r.Category = PaymentCategory.NotPayable; r.Problem = "Invoice is " + InvStatus.Label(detail.InvoiceStatus).ToLowerInvariant() + "."; continue; }
                if (detail.OverrideReason != null && detail.DuplicateCheckedBy == null) { r.Category = PaymentCategory.Manual; continue; }
                var before = pending.GetValueOrDefault(r.ItemId.Value);
                if (detail.SettledPaise + before + r.Settled > detail.EffectiveTotal)
                { r.Category = PaymentCategory.Overpay; r.Problem = $"Outstanding is {Paise.Format(detail.Outstanding - before)}."; continue; }
                pending[r.ItemId.Value] = before + r.Settled;
                r.Category = PaymentCategory.New;
            }
            catch (WorkflowException ex) { r.Category = PaymentCategory.Invalid; r.Problem = ex.Message; }
        }
        return rows;
    }

    public List<PaymentImportRow> PreviewPayments(string text, ClaimsPrincipal u, out string? error)
    {
        RequireAccounts(u);
        using var c = db.Open();
        return ClassifyPayments(c, null, ComplianceImport.Normalise(text), out error);
    }

    /// <summary>Imports the "matched — new" rows in one transaction (classification repeated inside it). Returns the count.</summary>
    public int ImportPayments(string text, ClaimsPrincipal u)
    {
        RequireAccounts(u);
        var norm = ComplianceImport.Normalise(text);
        var sha = ComplianceImport.Sha(norm);
        var n = InTx(db, (c, tx) =>
        {
            // the same file may come again (rows not payable last time); vouchers already recorded are skipped as "already recorded"
            var rows = ClassifyPayments(c, tx, norm, out var error);
            if (error != null) throw new WorkflowException(error);
            var ok = rows.Where(r => r.Category == PaymentCategory.New).ToList();
            if (ok.Count == 0) throw new WorkflowException("No row of this file can be imported.");
            foreach (var r in ok)
            {
                var it = Load(c, tx, r.ItemId!.Value);
                var inv = InvoiceOf(c, tx, it.Id)!;
                PaymentCore(c, tx, it, inv, ValidatePayment(r.Input, inv), false, "Import", u);
            }
            Db.Exec(c, tx, "INSERT INTO InwardImports (Kind, Sha256, Rows, ByName, At) VALUES ('Payments', @sha, @n, @by, @now)", new { sha, n = rows.Count, by = u.DisplayName(), now = Db.Now() });
            return ok.Count;
        });
        Touch(u.UserId());
        audit.Log("Inward.PaymentsImported", "InvoicePayments", null, $"{n} payment(s) imported");
        return n;
    }
}
