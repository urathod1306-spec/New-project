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

    /// <summary>"just now", "5 min ago", "3 h ago", "yesterday", "4 days ago", then the date.</summary>
    public string Relative(string? isoUtc)
    {
        if (string.IsNullOrEmpty(isoUtc) || !System.DateTime.TryParse(isoUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)) return "";
        var ago = System.DateTime.UtcNow - d;
        if (ago.TotalMinutes < 1) return "just now";
        if (ago.TotalMinutes < 60) return $"{(int)ago.TotalMinutes} min ago";
        if (ago.TotalHours < 24) return $"{(int)ago.TotalHours} h ago";
        var days = DateOnly.FromDateTime(LocalNow).DayNumber - DateOnly.FromDateTime(d + _offset).DayNumber;
        if (days <= 1) return "yesterday";
        if (days < 7) return $"{days} days ago";
        return (d + _offset).ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
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

    private static long _calls;

    /// <summary>True when <paramref name="key"/> already has <paramref name="max"/> hits within the window (records nothing).</summary>
    public static bool Blocked(string key, int max, TimeSpan window)
    {
        if (!Hits.TryGetValue(key, out var q)) return false;
        lock (q)
        {
            var now = System.DateTime.UtcNow;
            while (q.Count > 0 && now - q.Peek() > window) q.Dequeue();
            return q.Count >= max;
        }
    }

    /// <summary>Records one hit for <paramref name="key"/>.</summary>
    public static void Record(string key, TimeSpan window) => Allow(key, int.MaxValue, window);

    public static bool Allow(string key, int max, TimeSpan window)
    {
        // occasionally drop keys that have been idle for a day, so the table cannot grow without limit
        if (Interlocked.Increment(ref _calls) % 1000 == 0)
            foreach (var kv in Hits)
                lock (kv.Value) if (kv.Value.Count == 0 || System.DateTime.UtcNow - kv.Value.Last() > TimeSpan.FromDays(1)) Hits.TryRemove(kv.Key, out _);
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
