using System.Text;

namespace NabconsIntranet.Infrastructure;

public static class Csv
{
    /// <summary>Quotes a value for CSV and neutralises spreadsheet formula injection.</summary>
    public static string Escape(string? v)
    {
        v ??= "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0])) v = "'" + v;
        return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
    }

    /// <summary>Minimal RFC 4180 parser (quoted fields, embedded commas/quotes/newlines).</summary>
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }
            switch (ch)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    if (row.Any(f => f.Length > 0)) rows.Add(row);
                    row = new List<string>();
                    break;
                default: field.Append(ch); break;
            }
        }
        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0)) rows.Add(row);
        return rows;
    }
}
