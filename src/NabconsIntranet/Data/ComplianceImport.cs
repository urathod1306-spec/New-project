using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Data;

/// <summary>
/// Bulk import of action points from a CSV file (DD-07 §6 Import). Everything happens in memory: the uploaded bytes
/// are decoded, parsed and checked here and then dropped; nothing is written to disk. The confirm step re-validates
/// every row inside the insert transaction (ComplianceWorkflow.ImportPoints), so the preview checks are a convenience.
/// </summary>
public sealed class ComplianceImport(Db db, MasterRepository masters, ComplianceOptions o)
{
    public static readonly string[] Columns =
    {
        "RefNo", "RefSubject", "ActionPoint", "Category", "Priority", "OwnerVertical", "PrimaryOfficer", "SupportingOfficers",
        "OfficerCode", "RaisedOn", "DueDate", "Amount", "CarriedFrom", "Status", "ActionTakenSoFar", "AsOn",
    };
    public static readonly string[] Required = { "ActionPoint", "PrimaryOfficer", "DueDate" };

    public sealed record Row(int Line, CompliancePointInput Input, string Officers, List<string> Errors);

    /// <summary>Template with the header row and one synthetic example row.</summary>
    public static byte[] Template()
    {
        var sb = new StringBuilder(string.Join(",", Columns)).Append("\r\n");
        sb.Append(string.Join(",", new[]
        {
            "Memo No. 7", "Review of IT security policy", Csv.Escape("Place the revised IT security policy before the Board after vetting by the Risk Committee"),
            "IT & Systems", "High", "IT & Digital", "approver1", "", "GM(IT)", "12.09.2026", "31.12.2026", "", "", "Ongoing", "", "",
        }.Select(Csv.Escape))).Append("\r\n");
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    /// <summary>Decodes an uploaded file: UTF-8 only (with or without BOM), no NUL bytes. Returns null and a reason otherwise.</summary>
    public static string? Decode(byte[] bytes, out string? error)
    {
        error = null;
        if (bytes.Contains((byte)0)) { error = "This is not a text CSV file. In Excel use File → Save As → \"CSV UTF-8 (Comma delimited)\"."; return null; }
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { error = "The file is not saved as UTF-8. In Excel use File → Save As → \"CSV UTF-8 (Comma delimited)\"."; return null; }
    }

    /// <summary>The text whose SHA-256 identifies the file: BOM removed, line endings LF (browsers may change them).</summary>
    public static string Normalise(string text) => text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');

    public static string Sha(string normalised) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));

    public List<Row> Parse(string text, long actingUserId, out string? headerError)
    {
        headerError = null;
        text = Normalise(text);
        if (Encoding.UTF8.GetByteCount(text) > o.MaxImportKB * 1024L) { headerError = $"The file is larger than {o.MaxImportKB:N0} KB."; return new(); }
        var table = Csv.Parse(text);
        if (table.Count == 0) { headerError = "The file is empty."; return new(); }
        var header = table[0].Select(h => h.Trim()).ToList();
        var unknown = header.Where(h => h.Length > 0 && !Columns.Contains(h, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0) { headerError = "Unknown column(s): " + string.Join(", ", unknown) + ". Download the template for the column names."; return new(); }
        var missing = Required.Where(r => !header.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missing.Count > 0) { headerError = "Missing column(s): " + string.Join(", ", missing) + "."; return new(); }
        if (table.Count - 1 > o.MaxImportRows) { headerError = $"At most {o.MaxImportRows} rows per file (this one has {table.Count - 1})."; return new(); }

        var idx = Columns.ToDictionary(c => c, c => header.FindIndex(h => h.Equals(c, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
        var users = db.Query<PersonOption>("SELECT Id, DisplayName, Username FROM Users WHERE IsActive = 1")
            .GroupBy(x => x.Username, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var categories = masters.Names(MasterTypes.ComplianceCategory);
        var verticals = masters.Names(MasterTypes.Vertical);
        var rows = new List<Row>();
        for (var r = 1; r < table.Count; r++)
        {
            var cells = table[r];
            string? V(string col) => idx[col] >= 0 && idx[col] < cells.Count && !string.IsNullOrWhiteSpace(cells[idx[col]]) ? cells[idx[col]].Trim() : null;
            var errors = new List<string>();
            var input = new CompliancePointInput
            {
                RefNo = V("RefNo"), RefSubject = V("RefSubject"), PointText = V("ActionPoint"), Priority = V("Priority"),
                OfficerCode = V("OfficerCode"), RaisedOn = V("RaisedOn"), DueDate = V("DueDate"), CarriedFromNumber = V("CarriedFrom"),
                InitialStatus = V("Status"), InitialAtrText = V("ActionTakenSoFar"), InitialAsOn = V("AsOn"),
            };
            void Check(Action a) { try { a(); } catch (WorkflowException ex) { errors.Add(ex.Message); } }
            Check(() => ComplianceText.Required("Action point", input.PointText, ComplianceText.LongText));
            Check(() => ComplianceText.Clean("Reference no.", input.RefNo, 60));
            Check(() => ComplianceText.Clean("Subject", input.RefSubject, 300));
            Check(() => ComplianceText.Clean("Officer code", input.OfficerCode, 30));
            Check(() => ComplianceText.Clean("Action taken so far", input.InitialAtrText, ComplianceText.LongText));
            Check(() => ComplianceWorkflow.ParseDate("Due date", input.DueDate, true));
            Check(() => ComplianceWorkflow.ParseDate("Raised on", input.RaisedOn));
            Check(() => ComplianceWorkflow.ParseDate("As on", input.InitialAsOn));
            if (input.Priority != null && !CompliancePriority.All.Contains(input.Priority, StringComparer.OrdinalIgnoreCase)) errors.Add("Priority must be High, Medium or Low.");
            if (input.InitialStatus != null && input.InitialStatus is not (CStatus.Ongoing or CStatus.PendingExternal)) errors.Add("Status must be Ongoing or PendingExternal.");
            if (input.InitialAtrText != null && input.InitialAsOn == null) errors.Add("ActionTakenSoFar needs an AsOn date.");
            var cat = V("Category");
            if (cat != null) { input.Category = categories.FirstOrDefault(x => x.Equals(cat, StringComparison.OrdinalIgnoreCase)); if (input.Category == null) errors.Add($"Category '{cat}' is not in the list."); }
            var vert = V("OwnerVertical");
            if (vert != null) { input.OwnerVertical = verticals.FirstOrDefault(x => x.Equals(vert, StringComparison.OrdinalIgnoreCase)); if (input.OwnerVertical == null) errors.Add($"Vertical '{vert}' is not in the list."); }
            var amount = V("Amount");
            if (amount != null)
            {
                if (double.TryParse(amount.Replace(",", "").Replace("₹", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var a) && a >= 0) input.Amount = a;
                else errors.Add($"Amount '{amount}' is not a number.");
            }
            var names = new List<string>();
            var primary = V("PrimaryOfficer");
            if (primary == null) errors.Add("PrimaryOfficer is required (the officer's sign-in user name).");
            else if (users.TryGetValue(primary, out var pu)) { input.PrimaryOfficerUserId = pu.Id; names.Add(pu.DisplayName); }
            else errors.Add($"No active user with the sign-in name '{primary}'.");
            foreach (var s in (V("SupportingOfficers") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (users.TryGetValue(s, out var su)) { input.SupportOfficerIds.Add(su.Id); names.Add(su.DisplayName + " (supporting)"); }
                else errors.Add($"No active user with the sign-in name '{s}'.");
            }
            if (input.PrimaryOfficerUserId == actingUserId || input.SupportOfficerIds.Contains(actingUserId))
                errors.Add("You cannot make yourself a dealing officer on points you manage.");
            rows.Add(new Row(r + 1, input, string.Join(", ", names), errors));
        }
        return rows;
    }
}
