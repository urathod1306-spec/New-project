using SkiaSharp;

namespace NabconsIntranet.Infrastructure.Reports;

/// <summary>
/// PDF version of a printed report: A4 landscape, bordered tables with the heading row repeated on every page,
/// "Page x of y" footer. Uses SkiaSharp and HarfBuzz with the embedded Noto fonts (already in the package).
/// Long cells are split across pages line by line.
/// </summary>
public static class ReportPdfWriter
{
    private const float ML = 40, MR = 40, MT = 36, MB = 44;
    private const float Size = 8.5f, HeadSize = 8.5f, Pad = 3f;
    private static readonly SKColor Ink = new(0x1d, 0x27, 0x33), Muted = new(0x5d, 0x6b, 0x7a), Brand = new(0x0b, 0x4b, 0x28),
        Grid = new(0x80, 0x80, 0x80), HeadFill = new(0xe7, 0xee, 0xf5);

    private abstract record Op;
    private sealed record TextOp(float X, float Y, string Text, bool Bold, float Size, SKColor Color) : Op;
    private sealed record RectOp(SKRect R, SKColor? Fill, bool Stroke) : Op;

    private sealed class Layout(PdfFonts f, float W, float H)
    {
        private float CW => W - ML - MR;
        public readonly List<List<Op>> Pages = new() { new() };
        public float Y = MT;
        public void NewPage() { Pages.Add(new()); Y = MT; }
        public void Add(Op op) => Pages[^1].Add(op);
        public float Room => H - MB - Y;

        public void Centered(string text, bool bold, float size, SKColor color)
        {
            foreach (var line in f.Wrap(text, bold, size, CW))
            {
                var lh = size * 1.4f;
                if (Room < lh) NewPage();
                Add(new TextOp(ML + (CW - f.Width(line, bold, size)) / 2, Y + size, line, bold, size, color));
                Y += lh;
            }
        }

        public void Left(string text, bool bold, float size, SKColor color, float x = 0, float? width = null, bool right = false)
        {
            var w = width ?? CW;
            foreach (var line in f.Wrap(text, bold, size, w))
            {
                var lh = size * 1.4f;
                if (Room < lh) NewPage();
                var px = right ? ML + x + w - f.Width(line, bold, size) : ML + x;
                Add(new TextOp(px, Y + size, line, bold, size, color));
                Y += lh;
            }
        }

        public void Table(ReportSection s)
        {
            var total = s.IsSummary ? CW * 0.45f : CW;
            var sum = s.Columns.Sum(c => c.Width);
            var widths = s.Columns.Select(c => total * c.Width / (sum <= 0 ? 1 : sum)).ToArray();
            var lh = Size * 1.35f;
            var head = s.Columns.Select((c, i) => f.Wrap(c.Header, true, HeadSize, widths[i] - 2 * Pad)).ToArray();
            var headH = head.Max(l => l.Count) * lh + 2 * Pad;

            void Header()
            {
                if (Room < headH + lh + 2 * Pad) NewPage();
                var x = ML;
                for (var i = 0; i < widths.Length; i++)
                {
                    Add(new RectOp(new SKRect(x, Y, x + widths[i], Y + headH), HeadFill, true));
                    for (var k = 0; k < head[i].Count; k++) Add(new TextOp(x + Pad, Y + Pad + k * lh + HeadSize, head[i][k], true, HeadSize, Ink));
                    x += widths[i];
                }
                Y += headH;
            }

            Header();
            foreach (var row in s.Rows)
            {
                var cells = widths.Select((w, i) => f.Wrap(ReportDocument.CleanText(i < row.Cells.Count ? row.Cells[i] : ""), row.Bold, Size, w - 2 * Pad)).ToArray();
                var done = 0;                                           // lines already printed (a long row continues on the next page)
                var max = cells.Max(c => c.Count);
                while (done < max)
                {
                    var fit = (int)((Room - 2 * Pad) / lh);
                    if (fit < 1 || (done == 0 && fit < Math.Min(max, 3))) { NewPage(); Header(); continue; }
                    var take = Math.Min(fit, max - done);
                    var h = take * lh + 2 * Pad;
                    var x = ML;
                    for (var i = 0; i < widths.Length; i++)
                    {
                        Add(new RectOp(new SKRect(x, Y, x + widths[i], Y + h), null, true));
                        for (var k = 0; k < take && done + k < cells[i].Count; k++)
                            Add(new TextOp(x + Pad, Y + Pad + k * lh + Size, cells[i][done + k], row.Bold, Size, Ink));
                        x += widths[i];
                    }
                    Y += h;
                    done += take;
                    if (done < max) { NewPage(); Header(); }
                }
            }
            Y += 10;
        }
    }

    public static void Write(ReportDocument doc, Stream output)
    {
        using var fonts = new PdfFonts();
        var (W, H) = doc.Portrait ? (595f, 842f) : (842f, 595f);
        var CW = W - ML - MR;
        var L = new Layout(fonts, W, H);
        if (!string.IsNullOrWhiteSpace(doc.OrganisationLine)) L.Centered(doc.OrganisationLine, true, 12, Brand);
        L.Centered(doc.Title, true, 11.5f, Ink);
        foreach (var s in doc.Subtitles) L.Centered(s, false, 9.5f, Muted);
        L.Y += 8;
        foreach (var section in doc.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Heading))
            {
                if (L.Room < 60) L.NewPage();
                L.Left(section.Heading!, true, 10, Brand);
                L.Y += 2;
            }
            L.Table(section);
        }
        if (!string.IsNullOrWhiteSpace(doc.Signature)) { L.Y += 18; L.Left(doc.Signature!, true, 9.5f, Ink, right: true); }

        var when = DateTime.SpecifyKind(doc.GeneratedAt, DateTimeKind.Utc);
        using var doc2 = SKDocument.CreatePdf(output, new SKDocumentPdfMetadata
        {
            Title = doc.Title, Creator = "NABCONS Intranet", Producer = "NABCONS Intranet", Creation = when, Modified = when,
        });
        using var paint = new SKPaint { IsAntialias = true };
        var total = L.Pages.Count;
        for (var p = 0; p < total; p++)
        {
            var c = doc2.BeginPage(W, H);
            foreach (var op in L.Pages[p])
            {
                switch (op)
                {
                    case TextOp t: fonts.Draw(c, t.Text, t.X, t.Y, t.Bold, t.Size, t.Color, paint); break;
                    case RectOp r:
                        if (r.Fill is { } fill) { paint.Style = SKPaintStyle.Fill; paint.Color = fill; c.DrawRect(r.R, paint); }
                        if (r.Stroke) { paint.Style = SKPaintStyle.Stroke; paint.Color = Grid; paint.StrokeWidth = 0.5f; c.DrawRect(r.R, paint); }
                        break;
                }
            }
            paint.Style = SKPaintStyle.Stroke; paint.Color = new SKColor(0xdd, 0xe3, 0xea); paint.StrokeWidth = 0.6f;
            c.DrawLine(ML, H - MB + 14, W - MR, H - MB + 14, paint);
            var pg = $"Page {p + 1} of {total}";
            foreach (var (line, k) in fonts.Wrap(ReportDocument.CleanText(doc.Footer), false, 7.5f, CW - 80).Take(2).Select((l, k) => (l, k)))
                fonts.Draw(c, line, ML, H - MB + 25 + k * 10, false, 7.5f, Muted, paint);
            fonts.Draw(c, pg, W - MR - fonts.Width(pg, true, 7.5f), H - MB + 25, true, 7.5f, Muted, paint);
            doc2.EndPage();
        }
        doc2.Close();
    }
}
