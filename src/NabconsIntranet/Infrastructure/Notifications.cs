using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.Data.Sqlite;
using NabconsIntranet.Data;

namespace NabconsIntranet.Infrastructure;

/// <summary>Settings from the "Notifications" section of appsettings.json (safe defaults when absent).</summary>
public sealed class NotificationOptions
{
    /// <summary>How often open pages ask for new counts. Minimum 30 seconds.</summary>
    public int PollSeconds { get; set; } = 60;
    public int RetainReadDays { get; set; } = 90;
    public int RetainUnreadDays { get; set; } = 365;
    public int EffectivePollSeconds => Math.Max(30, PollSeconds);
}

public sealed class Notification
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Message { get; set; }
    public string? Link { get; set; }
    public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    public string? ActorName { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? ReadAt { get; set; }
    public bool IsRead => ReadAt != null;
    public string Module => Type.Contains('.') ? Type[..Type.IndexOf('.')] : Type;
}

/// <summary>What to tell people. Type is "Module.Event", e.g. "Request.Approved".</summary>
public sealed record NotificationMessage(string Type, string Title, string? Message, string? Link, string? EntityType = null, long? EntityId = null);

/// <summary>The person causing a notification (never notified about their own action). Id 0 = not signed in (e.g. a registration).</summary>
public sealed record NotificationActor(long UserId, string Name)
{
    public static NotificationActor From(ClaimsPrincipal u) => new(u.UserId(), u.DisplayName());
}

public static class NotificationModules
{
    public static readonly Dictionary<string, string> Labels = new()
    {
        ["Request"] = "Requests",
        ["Lead"] = "Business Leads",
        ["Admin"] = "Administration",
        ["Profile"] = "My Profile",
        ["ENote"] = "e-Notes",
        ["System"] = "System",
    };
}

/// <summary>
/// Keeps a "last changed" marker per user and per shared queue in memory, so that the badge poll can answer
/// "nothing changed" without touching the database. Markers are bumped on every write that affects a count.
/// (Single server process: the .exe, the Windows service, or IIS with its default single worker.)
/// </summary>
public sealed class ChangeTracker
{
    public const string AdminQueue = "t:admin";
    public const string LeadQueue = "t:leads";
    public const string ENoteQueue = "t:enotes";

    private long _seq;
    private readonly ConcurrentDictionary<string, (long Seq, long Ticks)> _keys = new();

    /// <summary>Changes on every restart, so an ETag from before a restart never matches.</summary>
    public string Boot { get; } = Guid.NewGuid().ToString("N")[..8];

    public void Touch(string key) => _keys[key] = (Interlocked.Increment(ref _seq), Environment.TickCount64);
    public void TouchUser(long userId) { if (userId > 0) Touch("u:" + userId); }

    public (long Seq, long Ticks) Latest(IEnumerable<string> keys)
    {
        long seq = 0, ticks = 0;
        foreach (var k in keys)
            if (_keys.TryGetValue(k, out var v)) { seq = Math.Max(seq, v.Seq); ticks = Math.Max(ticks, v.Ticks); }
        return (seq, ticks);
    }
}

public interface INotificationService
{
    /// <summary>Adds notifications inside the caller's transaction (committed or rolled back with the action). The actor is never notified.</summary>
    int Notify(SqliteConnection c, SqliteTransaction tx, IEnumerable<long> recipients, NotificationActor actor, NotificationMessage m);
    int NotifyAdministrators(SqliteConnection c, SqliteTransaction tx, NotificationActor actor, NotificationMessage m);
    int NotifyVertical(SqliteConnection c, SqliteTransaction tx, string vertical, NotificationActor actor, NotificationMessage m);
    long UnreadCount(long userId);
    List<Notification> Latest(long userId, int count);
    Paged<Notification> Page(long userId, bool unreadOnly, string? module, int page, int pageSize = 25);
    Notification? Open(long id, long userId);
    int MarkAllRead(long userId);
}

public sealed class NotificationService(Db db, ChangeTracker tracker) : INotificationService
{
    private const string Columns = "UserId, Type, Title, Message, Link, EntityType, EntityId, ActorName, CreatedAt";

    /// <summary>Only paths inside this intranet: "/x…", never "//host", "/\host", a scheme or control characters.</summary>
    public static bool IsSafeLink(string? link) =>
        link == null || (link.Length is > 0 and <= 400 && link[0] == '/' && (link.Length == 1 || (link[1] != '/' && link[1] != '\\'))
                         && !link.Any(char.IsControl) && !link.Contains('\\') && !link.Contains("://", StringComparison.Ordinal));

    private static object Args(long userId, NotificationActor actor, NotificationMessage m, string now) => new
    {
        uid = userId, m.Type, Title = Cut(m.Title, 200)!, Message = Cut(m.Message, 500), m.Link, m.EntityType, m.EntityId,
        ActorName = Cut(actor.Name, 200), now, actorId = actor.UserId,
    };

    private static string? Cut(string? s, int max) => s == null ? null : (s.Length > max ? s[..(max - 1)] + "…" : s);

    private static void Check(NotificationMessage m)
    {
        if (string.IsNullOrWhiteSpace(m.Type) || string.IsNullOrWhiteSpace(m.Title)) throw new ArgumentException("Notification type and title are required.");
        if (!IsSafeLink(m.Link)) throw new ArgumentException("Notification links must be relative links inside the intranet.");
    }

    public int Notify(SqliteConnection c, SqliteTransaction tx, IEnumerable<long> recipients, NotificationActor actor, NotificationMessage m)
    {
        Check(m);
        var now = Db.Now();
        var n = 0;
        foreach (var uid in recipients.Where(id => id > 0 && id != actor.UserId).Distinct())
        {
            n += Db.Exec(c, tx, $"""
                INSERT INTO Notifications ({Columns})
                SELECT @uid, @Type, @Title, @Message, @Link, @EntityType, @EntityId, @ActorName, @now
                WHERE EXISTS (SELECT 1 FROM Users WHERE Id = @uid AND IsActive = 1)
                """, Args(uid, actor, m, now));
            tracker.TouchUser(uid);
        }
        tracker.TouchUser(actor.UserId);
        return n;
    }

    public int NotifyAdministrators(SqliteConnection c, SqliteTransaction tx, NotificationActor actor, NotificationMessage m) =>
        NotifyWhere(c, tx, "(',' || Roles || ',') LIKE '%,Administrator,%'", null, actor, m);

    public int NotifyVertical(SqliteConnection c, SqliteTransaction tx, string vertical, NotificationActor actor, NotificationMessage m) =>
        string.IsNullOrWhiteSpace(vertical) ? 0 : NotifyWhere(c, tx, "Vertical = @vertical COLLATE NOCASE", vertical, actor, m);

    private int NotifyWhere(SqliteConnection c, SqliteTransaction tx, string where, string? vertical, NotificationActor actor, NotificationMessage m)
    {
        Check(m);
        var ids = Db.Query<IdRow>(c, tx, $"""
            INSERT INTO Notifications ({Columns})
            SELECT Id, @Type, @Title, @Message, @Link, @EntityType, @EntityId, @ActorName, @now
            FROM Users WHERE IsActive = 1 AND Id <> @actorId AND {where}
            RETURNING UserId AS Id
            """, new { m.Type, Title = Cut(m.Title, 200), Message = Cut(m.Message, 500), m.Link, m.EntityType, m.EntityId, ActorName = Cut(actor.Name, 200), now = Db.Now(), actorId = actor.UserId, vertical });
        foreach (var r in ids) tracker.TouchUser(r.Id);
        tracker.TouchUser(actor.UserId);
        return ids.Count;
    }

    private sealed class IdRow { public long Id { get; set; } }

    public long UnreadCount(long userId) =>
        db.Scalar<long>("SELECT COUNT(*) FROM Notifications WHERE UserId = @userId AND ReadAt IS NULL", new { userId });

    public List<Notification> Latest(long userId, int count) => db.Query<Notification>(
        "SELECT * FROM Notifications WHERE UserId = @userId ORDER BY CreatedAt DESC, Id DESC LIMIT @count", new { userId, count });

    public Paged<Notification> Page(long userId, bool unreadOnly, string? module, int page, int pageSize = 25)
    {
        page = Math.Max(1, page);
        module = module != null && NotificationModules.Labels.ContainsKey(module) ? module : null;
        var where = "UserId = @userId" + (unreadOnly ? " AND ReadAt IS NULL" : "") + (module != null ? " AND Type LIKE @prefix" : "");
        var args = new { userId, prefix = module + ".%", take = pageSize, skip = (page - 1) * pageSize };
        return new Paged<Notification>
        {
            Items = db.Query<Notification>($"SELECT * FROM Notifications WHERE {where} ORDER BY CreatedAt DESC, Id DESC LIMIT @take OFFSET @skip", args),
            Total = db.Scalar<long>($"SELECT COUNT(*) FROM Notifications WHERE {where}", args),
            Page = page,
            PageSize = pageSize,
        };
    }

    /// <summary>Returns the user's own notification and marks it read; null for anyone else's.</summary>
    public Notification? Open(long id, long userId)
    {
        var n = db.Single<Notification>("SELECT * FROM Notifications WHERE Id = @id AND UserId = @userId", new { id, userId });
        if (n == null) return null;
        if (n.ReadAt == null)
        {
            db.Exec("UPDATE Notifications SET ReadAt = @now WHERE Id = @id AND UserId = @userId AND ReadAt IS NULL", new { id, userId, now = Db.Now() });
            tracker.TouchUser(userId);
        }
        return n;
    }

    public int MarkAllRead(long userId)
    {
        var n = db.Exec("UPDATE Notifications SET ReadAt = @now WHERE UserId = @userId AND ReadAt IS NULL", new { userId, now = Db.Now() });
        tracker.TouchUser(userId);
        return n;
    }
}
