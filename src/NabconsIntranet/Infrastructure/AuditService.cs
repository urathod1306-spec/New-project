using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NabconsIntranet.Infrastructure;

public sealed class AuditEntry
{
    public long Id { get; set; }
    public string Timestamp { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Details { get; set; }
    public string? IpAddress { get; set; }
    public string PrevHash { get; set; } = "";
    public string Hash { get; set; } = "";
}

/// <summary>
/// Append-only, hash-chained audit log. Each entry stores the SHA-256 of the previous entry,
/// so any edit made outside the application (e.g. directly in the database file) is detectable
/// with "Verify integrity" on the Audit page.
/// </summary>
public sealed class AuditService
{
    private readonly Db _db;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<AuditService> _log;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    public const string Genesis = "GENESIS";

    /// <summary>Actor used for background work (seeding, command-line tools) where there is no web request.</summary>
    public static readonly AsyncLocal<string?> AmbientActor = new();

    public AuditService(Db db, IHttpContextAccessor http, ILogger<AuditService> log)
    {
        _db = db;
        _http = http;
        _log = log;
    }

    public void Log(string action, string? entityType = null, object? entityId = null, string? details = null, string? actor = null)
    {
        var ctx = _http.HttpContext;
        actor ??= ctx?.User?.Identity?.IsAuthenticated == true ? ctx.User.Identity!.Name! : AmbientActor.Value ?? (ctx != null ? "anonymous" : "system");
        var ip = ctx?.Connection.RemoteIpAddress?.ToString();
        var e = new AuditEntry
        {
            Timestamp = Db.Now(),
            Actor = actor,
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            Details = details,
            IpAddress = ip,
        };
        Gate.Wait();
        try
        {
            using var c = _db.Open();
            using var tx = c.BeginTransaction();
            e.PrevHash = Db.Scalar<string>(c, tx, "SELECT Hash FROM AuditLog ORDER BY Id DESC LIMIT 1") ?? Genesis;
            e.Hash = ComputeHash(e);
            Db.Insert(c, tx, """
                INSERT INTO AuditLog (Timestamp, Actor, Action, EntityType, EntityId, Details, IpAddress, PrevHash, Hash)
                VALUES (@Timestamp, @Actor, @Action, @EntityType, @EntityId, @Details, @IpAddress, @PrevHash, @Hash)
                """, e);
            tx.Commit();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to write audit entry {Action}", action);
            throw;
        }
        finally
        {
            Gate.Release();
        }
        _log.LogInformation("AUDIT {Actor} {Action} {EntityType} {EntityId} {Details}", actor, action, entityType, entityId, details);
    }

    public static string ComputeHash(AuditEntry e)
    {
        var payload = string.Join("|", e.PrevHash, e.Timestamp, e.Actor, e.Action, e.EntityType, e.EntityId, e.Details, e.IpAddress);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    public (bool ok, long checkedCount, long? brokenAtId) Verify()
    {
        using var c = _db.Open();
        var prev = Genesis;
        long count = 0;
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Timestamp, Actor, Action, EntityType, EntityId, Details, IpAddress, PrevHash, Hash FROM AuditLog ORDER BY Id";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var e = new AuditEntry
            {
                Id = r.GetInt64(0),
                Timestamp = r.GetString(1),
                Actor = r.GetString(2),
                Action = r.GetString(3),
                EntityType = r.IsDBNull(4) ? null : r.GetString(4),
                EntityId = r.IsDBNull(5) ? null : r.GetString(5),
                Details = r.IsDBNull(6) ? null : r.GetString(6),
                IpAddress = r.IsDBNull(7) ? null : r.GetString(7),
                PrevHash = r.GetString(8),
                Hash = r.GetString(9),
            };
            if (e.PrevHash != prev || ComputeHash(e) != e.Hash) return (false, count, e.Id);
            prev = e.Hash;
            count++;
        }
        return (true, count, null);
    }

    public void ExportCsv(TextWriter w, string? where = null, object? args = null)
    {
        w.WriteLine("Id,TimestampUtc,Actor,Action,EntityType,EntityId,Details,IpAddress,Hash");
        foreach (var e in _db.Query<AuditEntry>("SELECT * FROM AuditLog " + (where ?? "") + " ORDER BY Id", args))
        {
            w.WriteLine(string.Join(",", new[]
            {
                e.Id.ToString(CultureInfo.InvariantCulture), e.Timestamp, e.Actor, e.Action, e.EntityType, e.EntityId, e.Details, e.IpAddress, e.Hash,
            }.Select(Csv.Escape)));
        }
    }
}
