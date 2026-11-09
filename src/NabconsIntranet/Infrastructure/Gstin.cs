using System.Globalization;
using System.Text.RegularExpressions;

namespace NabconsIntranet.Infrastructure;

/// <summary>
/// Offline check of a GST identification number (15 characters): 2-digit state code, the holder's PAN (5 letters,
/// 4 digits, 1 letter), entity number, the letter Z (by default), and a check character computed with the
/// standard mod-36 algorithm. No network lookup is made.
/// </summary>
public static partial class Gstin
{
    private const string Chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [GeneratedRegex("^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][1-9A-Z][A-Z0-9][0-9A-Z]$")]
    private static partial Regex Shape();

    /// <summary>Upper-case, spaces removed; null when blank.</summary>
    public static string? Normalise(string? s)
    {
        var t = new string((s ?? "").Where(ch => !char.IsWhiteSpace(ch)).ToArray()).ToUpperInvariant();
        return t.Length == 0 ? null : t;
    }

    /// <summary>The expected check character for the first 14 characters.</summary>
    public static char CheckChar(string first14)
    {
        var sum = 0;
        for (var i = 0; i < 14; i++)
        {
            var v = Chars.IndexOf(first14[i]);
            var p = v * (i % 2 == 0 ? 1 : 2);
            sum += p / 36 + p % 36;
        }
        return Chars[(36 - sum % 36) % 36];
    }

    /// <summary>Null when valid; otherwise the reason.</summary>
    public static string? Problem(string? gstin)
    {
        var g = Normalise(gstin);
        if (g == null) return null;
        if (g.Length != 15) return "A GSTIN has 15 characters.";
        if (!Shape().IsMatch(g)) return "The GSTIN is not in the right format (state code, PAN, entity number, Z, check character).";
        var state = int.Parse(g[..2], CultureInfo.InvariantCulture);
        if (state is < 1 or > 38 && state is not (97 or 99)) return "The first two digits are not a valid state code.";
        if (CheckChar(g[..14]) != g[14]) return "The GSTIN check character is wrong; please re-check the number.";
        return null;
    }

    /// <summary>The holder's PAN (characters 3–12).</summary>
    public static string? Pan(string? gstin) => Normalise(gstin) is { Length: 15 } g ? g.Substring(2, 10) : null;
}

/// <summary>Money as whole paise (1/100 rupee), so sums compare exactly. Display with Indian digit grouping.</summary>
public static class Paise
{
    private static readonly CultureInfo In = CultureInfo.GetCultureInfo("en-IN");

    /// <summary>Parses "1,23,456.78", "₹ 500" or "500" into paise; null when blank; throws on nonsense or more than 2 decimals.</summary>
    public static long? Parse(string? s, string label)
    {
        var t = (s ?? "").Replace("₹", "").Replace(",", "").Replace(" ", "").Trim();
        if (t.Length == 0) return null;
        if (!decimal.TryParse(t, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var d))
            throw new NabconsIntranet.Data.WorkflowException($"{label}: enter an amount in rupees, e.g. 12,500.00.");
        if (decimal.Round(d, 2) != d) throw new NabconsIntranet.Data.WorkflowException($"{label}: at most two decimals (paise).");
        if (d < 0 || d > 100_000_000_000m) throw new NabconsIntranet.Data.WorkflowException($"{label}: the amount is not valid.");
        return (long)(d * 100);
    }

    public static string Format(long? paise) => paise.HasValue ? "₹ " + (paise.Value / 100m).ToString("N2", In) : "";
    public static string Plain(long? paise) => paise.HasValue ? (paise.Value / 100m).ToString("0.00", CultureInfo.InvariantCulture) : "";
    public static decimal Rupees(long paise) => paise / 100m;
}
