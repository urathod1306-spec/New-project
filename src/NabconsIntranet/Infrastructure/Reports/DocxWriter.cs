using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace NabconsIntranet.Infrastructure.Reports;

/// <summary>
/// Minimal, dependency-free Word (.docx) writer for printed reports: A4 landscape, bordered tables whose heading row
/// repeats on every page, and a footer with "Page x of y". Text is always written as plain runs (never fields or
/// macros), so cell content cannot execute. ZIP entries carry the document's fixed time, so the same model always
/// produces the same bytes.
/// </summary>
public static class DocxWriter
{
    private const int Long = 16838, Short = 11906, Margin = 850;           // twips: A4, 1.5 cm margins
    private static int TextW(ReportDocument d) => (d.Portrait ? Short : Long) - 2 * Margin;

    public static void Write(ReportDocument doc, Stream output)
    {
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
        Add("[Content_Types].xml", head + """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/word/footer1.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml"/></Types>""");
        Add("_rels/.rels", head + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/></Relationships>""");
        Add("word/_rels/document.xml.rels", head + """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer" Target="footer1.xml"/></Relationships>""");
        Add("word/styles.xml", head + """<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Calibri" w:hAnsi="Calibri" w:cs="Mangal" w:eastAsia="Calibri"/><w:sz w:val="20"/><w:szCs w:val="20"/><w:lang w:val="en-IN"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="60" w:line="252" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults><w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style><w:style w:type="table" w:default="1" w:styleId="TableNormal"><w:name w:val="Normal Table"/><w:tblPr><w:tblCellMar><w:top w:w="40" w:type="dxa"/><w:left w:w="80" w:type="dxa"/><w:bottom w:w="40" w:type="dxa"/><w:right w:w="80" w:type="dxa"/></w:tblCellMar></w:tblPr></w:style></w:styles>""");
        Add("word/footer1.xml", head + Footer(doc.Footer, TextW(doc)));
        Add("word/document.xml", head + Body(doc));
    }

    private static string X(string? s) => SecurityElement.Escape(ReportDocument.CleanText(s)) ?? "";

    /// <summary>Runs for a text, with line breaks kept.</summary>
    private static string Runs(string? text, bool bold = false, int halfPoints = 0)
    {
        var rpr = (bold ? "<w:b/>" : "") + (halfPoints > 0 ? $"<w:sz w:val=\"{halfPoints}\"/><w:szCs w:val=\"{halfPoints}\"/>" : "");
        var sb = new StringBuilder();
        var lines = ReportDocument.CleanText(text).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            sb.Append("<w:r>");
            if (rpr.Length > 0) sb.Append("<w:rPr>").Append(rpr).Append("</w:rPr>");
            if (i > 0) sb.Append("<w:br/>");
            sb.Append("<w:t xml:space=\"preserve\">").Append(SecurityElement.Escape(lines[i])).Append("</w:t></w:r>");
        }
        return sb.ToString();
    }

    private static string Para(string? text, string align = "left", bool bold = false, int halfPoints = 0, int after = 60, bool keepNext = false) =>
        $"<w:p><w:pPr>{(keepNext ? "<w:keepNext/>" : "")}<w:spacing w:after=\"{after}\"/><w:jc w:val=\"{align}\"/></w:pPr>{Runs(text, bold, halfPoints)}</w:p>";

    private static string Body(ReportDocument doc)
    {
        var sb = new StringBuilder("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><w:body>""");
        if (!string.IsNullOrWhiteSpace(doc.OrganisationLine)) sb.Append(Para(doc.OrganisationLine, "center", true, 24, 40));
        sb.Append(Para(doc.Title, "center", true, 24, 80));
        foreach (var s in doc.Subtitles) sb.Append(Para(s, "center", false, 20, 40));
        sb.Append(Para("", after: 40));
        foreach (var section in doc.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading)) sb.Append(Para(section.Heading, "left", true, 22, 80, keepNext: true));
            sb.Append(Table(section, TextW(doc)));
            sb.Append(Para("", after: 120));
        }
        if (!string.IsNullOrWhiteSpace(doc.Signature)) sb.Append(Para(doc.Signature, "right", true, 20, 0));
        sb.Append($"""<w:sectPr><w:footerReference w:type="default" r:id="rId2"/><w:pgSz w:w="{(doc.Portrait ? Short : Long)}" w:h="{(doc.Portrait ? Long : Short)}"{(doc.Portrait ? "" : " w:orient=\"landscape\"")}/><w:pgMar w:top="{Margin}" w:right="{Margin}" w:bottom="{Margin}" w:left="{Margin}" w:header="425" w:footer="425" w:gutter="0"/></w:sectPr></w:body></w:document>""");
        return sb.ToString();
    }

    private static string Table(ReportSection s, int textW)
    {
        var total = s.IsSummary ? textW * 0.45 : textW;
        var sum = s.Columns.Sum(c => c.Width);
        var widths = s.Columns.Select(c => (int)Math.Round(total * c.Width / (sum <= 0 ? 1 : sum))).ToList();
        var sb = new StringBuilder("<w:tbl><w:tblPr>");
        sb.Append($"<w:tblW w:w=\"{widths.Sum()}\" w:type=\"dxa\"/><w:tblLayout w:type=\"fixed\"/>");
        sb.Append("<w:tblBorders>");
        foreach (var b in new[] { "top", "left", "bottom", "right", "insideH", "insideV" }) sb.Append($"<w:{b} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"808080\"/>");
        sb.Append("</w:tblBorders></w:tblPr><w:tblGrid>");
        foreach (var w in widths) sb.Append($"<w:gridCol w:w=\"{w}\"/>");
        sb.Append("</w:tblGrid>");
        // heading row (repeats on each page)
        sb.Append("<w:tr><w:trPr><w:tblHeader/><w:cantSplit/></w:trPr>");
        for (var i = 0; i < s.Columns.Count; i++)
            sb.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{widths[i]}\" w:type=\"dxa\"/><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"E7EEF5\"/></w:tcPr>{Para(s.Columns[i].Header, "left", true, 20, 0)}</w:tc>");
        sb.Append("</w:tr>");
        foreach (var row in s.Rows)
        {
            sb.Append("<w:tr>");
            for (var i = 0; i < s.Columns.Count; i++)
            {
                var text = i < row.Cells.Count ? row.Cells[i] : "";
                sb.Append($"<w:tc><w:tcPr><w:tcW w:w=\"{widths[i]}\" w:type=\"dxa\"/></w:tcPr>{Para(text, "left", row.Bold, 0, 0)}</w:tc>");
            }
            sb.Append("</w:tr>");
        }
        sb.Append("</w:tbl>");
        return sb.ToString();
    }

    private static string Footer(string text, int textW)
    {
        static string Field(string code) =>
            $"<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> {code} </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r><w:r><w:t>1</w:t></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>";
        var tab = textW.ToString(CultureInfo.InvariantCulture);
        return "<w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:pPr><w:tabs><w:tab w:val=\"right\" w:pos=\"" + tab + "\"/></w:tabs></w:pPr>"
             + $"<w:r><w:rPr><w:sz w:val=\"16\"/></w:rPr><w:t xml:space=\"preserve\">{X(text)}</w:t></w:r><w:r><w:tab/></w:r>"
             + "<w:r><w:rPr><w:sz w:val=\"16\"/></w:rPr><w:t xml:space=\"preserve\">Page </w:t></w:r>" + Field("PAGE")
             + "<w:r><w:rPr><w:sz w:val=\"16\"/></w:rPr><w:t xml:space=\"preserve\"> of </w:t></w:r>" + Field("NUMPAGES")
             + "</w:p></w:ftr>";
    }
}
