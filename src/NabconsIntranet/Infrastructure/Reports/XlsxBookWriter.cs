using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace NabconsIntranet.Infrastructure.Reports;

/// <summary>
/// Excel (.xlsx) version of a printed report: one worksheet per section (summary tables go on the sheet of the
/// section before them), title lines above the table, a bold wrapped heading row, wrapped text cells, frozen
/// heading and auto-filter. Dependency-free like <see cref="XlsxWriter"/>; text is written as inline strings, never
/// formulas. Entries carry the document's fixed time, so the same model gives the same bytes.
/// </summary>
public static class XlsxBookWriter
{
    private sealed record Sheet(string Name, List<ReportSection> Tables);

    public static void Write(ReportDocument doc, Stream output)
    {
        // group: each non-summary section starts a sheet; summary sections stay with the sheet before them
        var sheets = new List<Sheet>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in doc.Sections)
        {
            if (s.IsSummary && sheets.Count > 0) { sheets[^1].Tables.Add(s); continue; }
            var name = SheetName(s.SheetName ?? s.Heading ?? $"Sheet{sheets.Count + 1}");
            var unique = name;
            for (var k = 2; !used.Add(unique); k++) unique = (name.Length > 27 ? name[..27] : name) + " (" + k + ")";
            sheets.Add(new Sheet(unique, new() { s }));
        }
        if (sheets.Count == 0) sheets.Add(new Sheet("Report", new()));

        var time = doc.GeneratedAt.Year < 1980 ? new DateTime(1980, 1, 1) : doc.GeneratedAt;
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        void Add(string path, string xml)
        {
            var e = zip.CreateEntry(path, CompressionLevel.Optimal);
            e.LastWriteTime = new DateTimeOffset(DateTime.SpecifyKind(time, DateTimeKind.Unspecified), TimeSpan.Zero);
            using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
            w.Write(xml);
        }
        const string head = """<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""";
        var types = new StringBuilder(head + """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>""");
        for (var i = 1; i <= sheets.Count; i++) types.Append($"""<Override PartName="/xl/worksheets/sheet{i}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>""");
        Add("[Content_Types].xml", types + "</Types>");
        Add("_rels/.rels", head + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
        var rels = new StringBuilder(head + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""");
        var book = new StringBuilder(head + """<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets>""");
        for (var i = 1; i <= sheets.Count; i++)
        {
            rels.Append($"""<Relationship Id="rId{i}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet{i}.xml"/>""");
            book.Append($"""<sheet name="{X(sheets[i - 1].Name)}" sheetId="{i}" r:id="rId{i}"/>""");
        }
        rels.Append($"""<Relationship Id="rId{sheets.Count + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""");
        Add("xl/_rels/workbook.xml.rels", rels.ToString());
        Add("xl/workbook.xml", book + "</sheets></workbook>");
        // styles: 0 normal, 1 bold heading (wrapped, shaded, bordered), 2 wrapped text (bordered, top), 3 bold title, 4 bold wrapped (bordered)
        Add("xl/styles.xml", head + """<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="3"><font><sz val="10"/><name val="Calibri"/></font><font><b/><sz val="10"/><name val="Calibri"/></font><font><b/><sz val="12"/><name val="Calibri"/></font></fonts><fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFE7EEF5"/><bgColor indexed="64"/></patternFill></fill></fills><borders count="2"><border><left/><right/><top/><bottom/><diagonal/></border><border><left style="thin"><color rgb="FF808080"/></left><right style="thin"><color rgb="FF808080"/></right><top style="thin"><color rgb="FF808080"/></top><bottom style="thin"><color rgb="FF808080"/></bottom><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="5"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf><xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf><xf numFmtId="0" fontId="2" fillId="0" borderId="0" xfId="0" applyFont="1"/><xf numFmtId="0" fontId="1" fillId="0" borderId="1" xfId="0" applyFont="1" applyBorder="1" applyAlignment="1"><alignment vertical="top" wrapText="1"/></xf></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>""");
        for (var i = 0; i < sheets.Count; i++) Add($"xl/worksheets/sheet{i + 1}.xml", SheetXml(doc, sheets[i], i == 0));
    }

    private static string SheetXml(ReportDocument doc, Sheet sheet, bool first)
    {
        var main = sheet.Tables.FirstOrDefault();
        var cols = main?.Columns ?? new();
        var sb = new StringBuilder();
        var r = 0;
        int headerRow = 0, lastDataRow = 0;
        void Text(int c, string? v, int style) =>
            sb.Append($"<c r=\"{Col(c)}{r}\" t=\"inlineStr\" s=\"{style}\"><is><t xml:space=\"preserve\">{X(v)}</t></is></c>");
        void Title(string? v, int style) { if (string.IsNullOrWhiteSpace(v)) return; r++; sb.Append($"<row r=\"{r}\">"); Text(0, v, style); sb.Append("</row>"); }

        Title(doc.OrganisationLine, 3);
        Title(doc.Title, 3);
        foreach (var s in doc.Subtitles) Title(s, 0);
        foreach (var table in sheet.Tables)
        {
            if (r > 0) r++;                                                     // blank row before each table
            Title(table.Heading, 3);
            r++;
            if (table == main) headerRow = r;
            sb.Append($"<row r=\"{r}\">");
            for (var c = 0; c < table.Columns.Count; c++) Text(c, table.Columns[c].Header, 1);
            sb.Append("</row>");
            foreach (var row in table.Rows)
            {
                r++;
                sb.Append($"<row r=\"{r}\">");
                for (var c = 0; c < table.Columns.Count; c++)
                {
                    var v = c < row.Cells.Count ? row.Cells[c] : "";
                    var style = row.Bold ? 4 : 2;
                    if (table.IsSummary && double.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                        sb.Append($"<c r=\"{Col(c)}{r}\" s=\"{style}\"><v>{n.ToString(CultureInfo.InvariantCulture)}</v></c>");
                    else Text(c, v, style);
                }
                sb.Append("</row>");
            }
            if (table == main) lastDataRow = r;
        }
        if (first && !string.IsNullOrWhiteSpace(doc.Signature)) { r += 2; Title(doc.Signature!.Replace("\n", " · "), 3); }
        if (!string.IsNullOrWhiteSpace(doc.Footer)) { r++; Title(doc.Footer, 0); }

        // column widths: relative widths scaled to about 160 characters in total
        var sum = cols.Sum(c => c.Width);
        var colXml = new StringBuilder();
        if (cols.Count > 0)
        {
            colXml.Append("<cols>");
            for (var c = 0; c < cols.Count; c++)
            {
                var w = Math.Max(8, Math.Round(160 * cols[c].Width / (sum <= 0 ? 1 : sum), 1));
                colXml.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{w.ToString(CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
            }
            colXml.Append("</cols>");
        }
        var pane = headerRow > 0 ? $"<pane ySplit=\"{headerRow}\" topLeftCell=\"A{headerRow + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/>" : "";
        var filter = headerRow > 0 && cols.Count > 0 ? $"<autoFilter ref=\"A{headerRow}:{Col(cols.Count - 1)}{Math.Max(headerRow, lastDataRow)}\"/>" : "";
        return """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">"""
             + $"<sheetViews><sheetView workbookViewId=\"0\">{pane}</sheetView></sheetViews>{colXml}<sheetData>{sb}</sheetData>{filter}"
             + """<pageMargins left="0.4" right="0.4" top="0.5" bottom="0.5" header="0.3" footer="0.3"/><pageSetup paperSize="9" orientation="landscape" fitToWidth="1" fitToHeight="0"/></worksheet>""";
    }

    /// <summary>Excel sheet names: at most 31 characters, none of : \ / ? * [ ].</summary>
    private static string SheetName(string s)
    {
        var clean = new string(ReportDocument.CleanText(s).Where(ch => ch is not (':' or '\\' or '/' or '?' or '*' or '[' or ']') && ch != '\n').ToArray()).Trim('\'', ' ');
        if (clean.Length == 0) clean = "Sheet";
        return clean.Length > 31 ? clean[..31] : clean;
    }

    private static string Col(int i)
    {
        var s = "";
        for (i++; i > 0; i = (i - 1) / 26) s = (char)('A' + (i - 1) % 26) + s;
        return s;
    }

    private static string X(string? s) => SecurityElement.Escape(ReportDocument.CleanText(s)) ?? "";
}
