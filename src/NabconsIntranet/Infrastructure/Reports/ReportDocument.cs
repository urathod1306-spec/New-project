using System.Text;

namespace NabconsIntranet.Infrastructure.Reports;

/// <summary>A column of a printed table; <see cref="Width"/> is relative to the other columns of the same table.</summary>
public sealed record ReportColumn(string Header, float Width);

/// <summary>One printed table row. Cell texts may contain line breaks ("\n").</summary>
public sealed record ReportRow(IReadOnlyList<string> Cells, bool Bold = false);

/// <summary>A heading followed by one table (an annexure, a sheet, or a summary table).</summary>
public sealed class ReportSection
{
    public string? Heading { get; init; }
    public List<ReportColumn> Columns { get; init; } = new();
    public List<ReportRow> Rows { get; init; } = new();
    /// <summary>Small tables (e.g. Completed / Ongoing / Total) are printed narrower, without repeating the heading row.</summary>
    public bool IsSummary { get; init; }
    /// <summary>Sheet name for the XLSX output (max 31 characters).</summary>
    public string? SheetName { get; init; }
}

/// <summary>
/// Everything a printed report contains, independent of the output format. The DOCX, PDF and XLSX writers render
/// only this model, so the same model always gives the same output (this is what makes ATR snapshots reproducible).
/// </summary>
public sealed class ReportDocument
{
    public string OrganisationLine { get; init; } = "";
    public string Title { get; init; } = "";
    public List<string> Subtitles { get; init; } = new();
    public List<ReportSection> Sections { get; init; } = new();
    /// <summary>Printed at the end, right-aligned (lines separated by "\n").</summary>
    public string? Signature { get; init; }
    /// <summary>Printed on every page, left of "Page x of y".</summary>
    public string Footer { get; init; } = "";
    /// <summary>Fixed generation time (UTC); used for file metadata so output does not depend on the clock.</summary>
    public DateTime GeneratedAt { get; init; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    /// <summary>File name without extension.</summary>
    public string FileBase { get; init; } = "report";

    /// <summary>Removes characters that are not allowed in XML 1.0 or would break a PDF line (control characters except tab and newline).</summary>
    public static string CleanText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Replace("\r\n", "\n").Replace('\r', '\n'))
            if (ch == '\n' || ch == '\t' || (ch >= 0x20 && ch != 0xFFFE && ch != 0xFFFF)) sb.Append(ch);
        return sb.ToString();
    }
}

/// <summary>At most two reports (DOCX, PDF, XLSX) are generated at the same time, to protect the server PC.</summary>
public static class ReportGate
{
    private static readonly SemaphoreSlim Slots = new(2, 2);

    /// <summary>Waits up to <paramref name="wait"/> for a slot; returns null when the generator stays busy.</summary>
    public static async Task<IDisposable?> EnterAsync(TimeSpan wait, CancellationToken ct = default)
    {
        if (!await Slots.WaitAsync(wait, ct)) return null;
        return new Releaser();
    }

    /// <summary>Slots currently free (for tests).</summary>
    public static int Free => Slots.CurrentCount;

    private sealed class Releaser : IDisposable
    {
        private int _done;
        public void Dispose() { if (Interlocked.Exchange(ref _done, 1) == 0) Slots.Release(); }
    }
}

/// <summary>Output formats offered for printed reports.</summary>
public static class ReportFormat
{
    public const string Docx = "docx", Pdf = "pdf", Xlsx = "xlsx";
    public static readonly string[] All = { Docx, Pdf, Xlsx };

    public static string ContentType(string f) => f switch
    {
        Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        Pdf => "application/pdf",
        _ => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>Renders the document in the given format to <paramref name="output"/>.</summary>
    public static void Write(ReportDocument doc, string format, Stream output)
    {
        switch (format)
        {
            case Docx: DocxWriter.Write(doc, output); break;
            case Pdf: ReportPdfWriter.Write(doc, output); break;
            case Xlsx: XlsxBookWriter.Write(doc, output); break;
            default: throw new ArgumentException("Unknown format " + format);
        }
    }
}
