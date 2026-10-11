using System.Text;

namespace NabconsIntranet.Infrastructure;

/// <summary>Generates small single-page text PDFs for the synthetic demo circulars.</summary>
public static class SimplePdf
{
    public static byte[] Create(string title, IEnumerable<string> lines)
    {
        var content = new StringBuilder();
        content.Append("BT\n/F2 16 Tf\n50 790 Td\n(").Append(Esc(title)).Append(") Tj\n/F1 11 Tf\n0 -28 Td\n");
        foreach (var raw in lines)
        {
            foreach (var line in Wrap(raw, 95))
                content.Append('(').Append(Esc(line)).Append(") Tj\n0 -16 Td\n");
        }
        content.Append("ET\n");
        var stream = Encoding.ASCII.GetBytes(content.ToString());

        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>",
            $"<< /Length {stream.Length} >>\nstream\n{Encoding.ASCII.GetString(stream)}endstream",
        };

        using var ms = new MemoryStream();
        void W(string s) { var b = Encoding.ASCII.GetBytes(s); ms.Write(b, 0, b.Length); }
        W("%PDF-1.4\n");
        var offsets = new List<long>();
        for (int i = 0; i < objects.Count; i++)
        {
            offsets.Add(ms.Position);
            W($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = ms.Position;
        W($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:D10} 00000 n \n");
        W($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    private static string Esc(string s)
    {
        var ascii = new string(s.Select(ch => ch < 128 ? ch : '-').ToArray());
        return ascii.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static IEnumerable<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text)) { yield return ""; yield break; }
        var line = new StringBuilder();
        foreach (var word in text.Split(' '))
        {
            if (line.Length + word.Length + 1 > width && line.Length > 0)
            {
                yield return line.ToString();
                line.Clear();
            }
            if (line.Length > 0) line.Append(' ');
            line.Append(word);
        }
        if (line.Length > 0) yield return line.ToString();
    }
}
