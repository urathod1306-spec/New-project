using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NabconsIntranet.Infrastructure;

/// <summary>Display formatting. Timestamps are stored in UTC and shown in local office time.</summary>
public sealed class Fmt
{
    private readonly TimeSpan _offset;
    private static readonly CultureInfo In = CultureInfo.GetCultureInfo("en-IN");

    public Fmt(IntranetOptions o) => _offset = TimeSpan.FromMinutes(o.DisplayUtcOffsetMinutes);

    public System.DateTime LocalNow => System.DateTime.UtcNow + _offset;
    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    public string DateTime(string? isoUtc)
    {
        if (string.IsNullOrEmpty(isoUtc)) return "";
        if (!System.DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)) return isoUtc;
        return (d + _offset).ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture);
    }

    public string Date(string? isoDate)
    {
        if (string.IsNullOrEmpty(isoDate)) return "";
        if (isoDate.Length > 10)
        {
            if (System.DateTime.TryParse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
                return (dt + _offset).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }
        return System.DateTime.TryParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
            : isoDate;
    }

    public static string Money(double? amount) => amount.HasValue ? "₹ " + amount.Value.ToString("N2", In) : "";

    public static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / 1024.0 / 1024.0:0.##} MB",
    };

    public static string Initials(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    public double DaysSince(string? isoUtc)
    {
        if (string.IsNullOrEmpty(isoUtc) || !System.DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)) return 0;
        return Math.Floor((System.DateTime.UtcNow - d).TotalDays);
    }
}

public static class SearchText
{
    private static readonly Regex Token = new(@"[\p{L}\p{N}]+", RegexOptions.Compiled);

    /// <summary>
    /// Turns free text into a safe FTS5 query: only letters/digits survive, every term is quoted
    /// and prefix-matched, so malformed input can never break the query syntax.
    /// </summary>
    public static string? ToFtsQuery(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var terms = Token.Matches(input.Length > 200 ? input[..200] : input)
            .Select(m => m.Value).Where(t => t.Length > 0).Take(10).ToList();
        if (terms.Count == 0) return null;
        return string.Join(" AND ", terms.Select(t => "\"" + t + "\"*"));
    }

    /// <summary>Escapes a value for a SQL LIKE pattern (used with ESCAPE '\').</summary>
    public static string Like(string? input)
    {
        var s = (input ?? "").Trim();
        if (s.Length > 100) s = s[..100];
        var sb = new StringBuilder("%");
        foreach (var ch in s)
        {
            if (ch is '%' or '_' or '\\') sb.Append('\\');
            sb.Append(ch);
        }
        return sb.Append('%').ToString();
    }
}

/// <summary>Writes daily rolling log files into the logs folder (no third-party logging library).</summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _folder;
    private readonly object _lock = new();

    public FileLoggerProvider(string folder)
    {
        _folder = folder;
        Directory.CreateDirectory(folder);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { }

    internal void Write(string line)
    {
        var path = Path.Combine(_folder, $"intranet-{DateTime.Now:yyyyMMdd}.log");
        lock (_lock)
        {
            try { File.AppendAllText(path, line + Environment.NewLine); } catch { /* never crash on logging */ }
        }
    }

    private sealed class FileLogger(FileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception != null) msg += Environment.NewLine + exception;
            owner.Write(msg);
        }
    }
}

/// <summary>Simple per-IP limiter for anonymous actions (e.g. registration look-ups), so mobile numbers cannot be probed in bulk.</summary>
public static class Throttle
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Queue<System.DateTime>> Hits = new();

    public static bool Allow(string key, int max, TimeSpan window)
    {
        var q = Hits.GetOrAdd(key, _ => new Queue<System.DateTime>());
        lock (q)
        {
            var now = System.DateTime.UtcNow;
            while (q.Count > 0 && now - q.Peek() > window) q.Dequeue();
            if (q.Count >= max) return false;
            q.Enqueue(now);
            return true;
        }
    }
}
