using System.Globalization;
using System.Reflection;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace NabconsIntranet.Infrastructure;

/// <summary>
/// The Noto Sans and Noto Sans Devanagari fonts embedded in the program, with HarfBuzz shapers, so Hindi and English
/// print correctly in generated PDFs without any installed fonts. Shared by the e-Note PDF and the report PDFs.
/// </summary>
public sealed class PdfFonts : IDisposable
{
    public readonly SKTypeface Latin, LatinBold, Deva, DevaBold;
    public readonly SKShaper SLatin, SLatinBold, SDeva, SDevaBold;
    private readonly Dictionary<(string, bool, float), float> _widths = new();

    public PdfFonts()
    {
        Latin = Load("NotoSans-Regular.ttf"); LatinBold = Load("NotoSans-Bold.ttf");
        Deva = Load("NotoSansDevanagari-Regular.ttf"); DevaBold = Load("NotoSansDevanagari-Bold.ttf");
        SLatin = new SKShaper(Latin); SLatinBold = new SKShaper(LatinBold); SDeva = new SKShaper(Deva); SDevaBold = new SKShaper(DevaBold);
    }

    private static SKTypeface Load(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Fonts." + name) ?? throw new InvalidOperationException("Missing font " + name);
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return SKTypeface.FromData(SKData.CreateCopy(ms.ToArray()));
    }

    public static bool IsDeva(char ch) => ch is >= 'ऀ' and <= 'ॿ' or >= '꣠' and <= 'ꣿ' or >= '᳐' and <= '᳿' or '‌' or '‍';

    /// <summary>Splits text into runs of Devanagari / other, so each run is shaped with the right font.</summary>
    public static IEnumerable<(string text, bool deva)> Runs(string s)
    {
        if (s.Length == 0) yield break;
        var sb = new StringBuilder();
        bool? cur = null;
        foreach (var ch in s)
        {
            var d = IsDeva(ch) || (cur == true && (char.GetUnicodeCategory(ch) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark));
            if (cur != null && d != cur) { yield return (sb.ToString(), cur.Value); sb.Clear(); }
            cur = d; sb.Append(ch);
        }
        if (sb.Length > 0) yield return (sb.ToString(), cur!.Value);
    }

    public (SKShaper shaper, SKFont font) Font(bool deva, bool bold, float size)
    {
        var tf = deva ? (bold ? DevaBold : Deva) : (bold ? LatinBold : Latin);
        var sh = deva ? (bold ? SDevaBold : SDeva) : (bold ? SLatinBold : SLatin);
        return (sh, new SKFont(tf, size) { Subpixel = true });
    }

    public float Width(string text, bool bold, float size)
    {
        if (_widths.TryGetValue((text, bold, size), out var w)) return w;
        w = 0;
        foreach (var (run, deva) in Runs(text))
        {
            var (sh, font) = Font(deva, bold, size);
            using (font) w += sh.Shape(run, font).Width;
        }
        return _widths[(text, bold, size)] = w;
    }

    /// <summary>Greedy word wrap; words longer than the line (e.g. hashes, paths) are broken by character.</summary>
    public List<string> Wrap(string text, bool bold, float size, float width)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = "";
            foreach (var word0 in raw.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var word = word0;
                while (Width(word, bold, size) > width)
                {
                    if (line.Length > 0) { lines.Add(line); line = ""; }
                    var e = StringInfo.GetTextElementEnumerator(word);
                    var take = new StringBuilder();
                    while (e.MoveNext() && Width(take + (string)e.Current, bold, size) <= width) take.Append((string)e.Current);
                    if (take.Length == 0) take.Append(word[0]);
                    lines.Add(take.ToString());
                    word = word[take.Length..];
                }
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Width(candidate, bold, size) <= width) line = candidate;
                else { lines.Add(line); line = word; }
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Draws text at a baseline position, shaping each script run with its own font.</summary>
    public void Draw(SKCanvas c, string text, float x, float y, bool bold, float size, SKColor color, SKPaint paint)
    {
        paint.Color = color;
        paint.Style = SKPaintStyle.Fill;
        foreach (var (run, deva) in Runs(text))
        {
            var (sh, font) = Font(deva, bold, size);
            using (font)
            {
                c.DrawShapedText(sh, run, x, y, SKTextAlign.Left, font, paint);
                x += sh.Shape(run, font).Width;
            }
        }
    }

    public void Dispose()
    {
        foreach (var s in new[] { SLatin, SLatinBold, SDeva, SDevaBold }) s.Dispose();
        foreach (var t in new[] { Latin, LatinBold, Deva, DevaBold }) t.Dispose();
    }
}
