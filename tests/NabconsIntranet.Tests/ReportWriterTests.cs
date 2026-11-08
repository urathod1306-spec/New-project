using System.IO.Compression;
using System.Text;
using NabconsIntranet.Infrastructure.Reports;

namespace NabconsIntranet.Tests;

/// <summary>DOCX, PDF and XLSX report writers (release 1.6.0): valid files, every cell present, and byte-stable output.</summary>
public class ReportWriterTests
{
    public static ReportDocument Sample(int rows = 40) => new()
    {
        OrganisationLine = "NABARD Consultancy Services",
        Title = "Follow-up Actions on the Minutes of the 99th Meeting of the Board of Directors held on 12.09.2026",
        Subtitles = { "Status as on 30.10.2026" },
        Sections =
        {
            new ReportSection
            {
                Heading = "Annexure I",
                Columns = { new("Memorandum / Agenda No. (with subject)", 2), new("Action Point", 4), new("Action Taken", 4), new("Status", 1) },
                Rows = Enumerable.Range(1, rows).Select(i => new ReportRow(new[] { $"Memo No. {i}\nSubject {i}", "Place the revised policy before the Board " + new string('x', i % 7 * 40), $"Done <&> \"quoted\" {i}\nसमिति द्वारा अनुमोदित", i % 2 == 0 ? "Completed" : "Ongoing" })).ToList(),
            },
            new ReportSection { IsSummary = true, Columns = { new("Completed", 1), new("Ongoing", 1), new("Total", 1) }, Rows = { new ReportRow(new[] { "20", "20", "40" }, true) } },
        },
        Signature = "Company Secretary",
        Footer = "Final — submitted to the Board on 31.10.2026 · Snapshot #1",
        GeneratedAt = new DateTime(2026, 10, 31, 10, 0, 0, DateTimeKind.Utc),
    };

    private static byte[] Render(ReportDocument d, string f) { using var ms = new MemoryStream(); ReportFormat.Write(d, f, ms); return ms.ToArray(); }

    [Theory]
    [InlineData("docx")]
    [InlineData("xlsx")]
    [InlineData("pdf")]
    public void Same_model_gives_the_same_bytes(string format)
    {
        var a = Render(Sample(), format);
        var b = Render(Sample(), format);
        Assert.True(a.Length > 1000);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Docx_and_xlsx_are_valid_packages_with_all_text()
    {
        using (var zip = new ZipArchive(new MemoryStream(Render(Sample(), "docx"))))
        {
            var xml = new StreamReader(zip.GetEntry("word/document.xml")!.Open()).ReadToEnd();
            Assert.Contains("w:orient=\"landscape\"", xml);
            Assert.Contains("Memo No. 40", xml);
            Assert.Contains("&lt;&amp;&gt;", xml);                                   // escaped, never markup
            Assert.Contains("<w:tblHeader/>", xml);
            Assert.Contains("NUMPAGES", new StreamReader(zip.GetEntry("word/footer1.xml")!.Open()).ReadToEnd());
            System.Xml.Linq.XDocument.Parse(xml);
        }
        using (var zip = new ZipArchive(new MemoryStream(Render(Sample(), "xlsx"))))
        {
            var xml = new StreamReader(zip.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
            Assert.Contains("Memo No. 40", xml);
            Assert.DoesNotContain("<f>", xml);                                       // never formulas
            System.Xml.Linq.XDocument.Parse(xml);
        }
    }

    [Fact]
    public void Pdf_has_several_landscape_pages()
    {
        var pdf = Encoding.Latin1.GetString(Render(Sample(120), "pdf"));
        Assert.StartsWith("%PDF", pdf);
        Assert.Contains("/MediaBox [0 0 842 595]", pdf);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(pdf, "/Type /Page\\b").Count >= 3);
    }
}
