using Microsoft.AspNetCore.Mvc;
using NabconsIntranet.Data;
using NabconsIntranet.Infrastructure;

namespace NabconsIntranet.Pages.ENotes;

/// <summary>
/// The e-Note register for Administrators and Auditors: every submitted note with its ageing — metadata only.
/// It never shows a note's text, comments or attachments (they follow the normal visibility rules).
/// </summary>
public class RegisterModel(ENoteRepository notes, MasterRepository masters, AuditService audit, Fmt fmt) : ENotePage
{
    public ENoteFilter Filter { get; private set; } = new();
    public Paged<ENoteRow> Result { get; private set; } = new();
    public List<string> Categories { get; private set; } = new();
    public List<string> Verticals { get; private set; } = new();

    public void OnGet(string? q, string? state, string? category, string? vertical, string? from, string? to, string? initiator, string? holder, int page = 1)
    {
        Filter = ReadFilter("register", q, state, category, vertical, from, to, initiator, holder, page);
        Result = notes.Register(Filter);
        Categories = masters.Names(MasterTypes.ENoteCategory);
        Verticals = masters.Names(MasterTypes.Vertical);
    }

    public string PageUrl(int page) => "/ENotes/Register" + Query(Filter, page, includeTab: false);

    public double HeldDays(ENoteRow n) => ENoteStates.Pending.Contains(n.State) ? fmt.DaysSince(n.HeldSince) : 0;

    public double TotalDays(ENoteRow n)
    {
        if (n.SubmittedAt == null) return 0;
        var end = n.DecidedAt ?? n.ClosedAt;
        if (end == null) return fmt.DaysSince(n.SubmittedAt);
        return Math.Floor((DateTime.Parse(end, null, System.Globalization.DateTimeStyles.RoundtripKind) - DateTime.Parse(n.SubmittedAt, null, System.Globalization.DateTimeStyles.RoundtripKind)).TotalDays);
    }

    public static string SubjectFor(ENoteRow n) => n.IsConfidential ? "(confidential)" : n.Subject;

    public IActionResult OnGetExport(string? q, string? state, string? category, string? vertical, string? from, string? to, string? initiator, string? holder)
    {
        var f = ReadFilter("register", q, state, category, vertical, from, to, initiator, holder, 1);
        var rows = notes.RegisterAll(f).Select(n => (IReadOnlyList<object?>)new object?[]
        {
            n.NoteNumber, SubjectFor(n), n.Category, n.InitiatorName, n.InitiatorVertical, ENoteStates.Label(n.State), n.Outcome, n.IsConfidential ? "Yes" : "No",
            n.CurrentHolderName, HeldDays(n), TotalDays(n), fmt.DateTime(n.SubmittedAt), fmt.DateTime(n.DecidedAt), fmt.DateTime(n.ClosedAt),
        }).ToList();
        var bytes = XlsxWriter.Build("e-Note register", new[]
        {
            "Note number", "Subject", "Category", "Initiator", "Initiator vertical", "State", "Outcome", "Confidential", "Current holder",
            "Days with current holder", "Total days", "Submitted", "Decided", "Closed",
        }, rows);
        audit.Log("ENote.RegisterExported", "ENote", null, $"{rows.Count} rows (state={f.State}; category={f.Category}; q={f.Q})");
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"enote-register-{DateTime.Now:yyyyMMdd-HHmm}.xlsx");
    }
}
