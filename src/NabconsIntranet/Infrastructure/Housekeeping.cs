namespace NabconsIntranet.Infrastructure;

public sealed record HousekeepingResult(int ReadDeleted, int UnreadDeleted, List<string> Notes);

/// <summary>
/// Daily tidy-up, run by the scheduler straight after the daily backup (outside office hours).
/// Notifications are a convenience, not a record: the audit log remains the system of record.
/// Deletes run in small batches so the database is never locked for long.
/// </summary>
public sealed class HousekeepingService(Db db, NotificationOptions no, AuditService audit, ILogger<HousekeepingService> log)
{
    private const int Batch = 5000;

    /// <summary>Extra daily steps added by modules (e-Note: ageing alerts, orphan-file report).</summary>
    public List<Func<List<string>>> ExtraSteps { get; } = new();

    public HousekeepingResult Run(DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        var readCut = now.AddDays(-Math.Max(1, no.RetainReadDays)).ToString("o");
        var unreadCut = now.AddDays(-Math.Max(1, no.RetainUnreadDays)).ToString("o");
        var read = DeleteInBatches("ReadAt IS NOT NULL AND CreatedAt < @cut", readCut);
        var unread = DeleteInBatches("ReadAt IS NULL AND CreatedAt < @cut", unreadCut);
        var notes = new List<string>();
        foreach (var step in ExtraSteps)
        {
            try { notes.AddRange(step()); }
            catch (Exception ex) { log.LogError(ex, "Housekeeping step failed"); notes.Add("A housekeeping step failed: " + ex.Message); }
        }
        audit.Log("System.Housekeeping", "System", null,
            $"Notifications removed: {read} read (older than {no.RetainReadDays} days), {unread} unread (older than {no.RetainUnreadDays} days)" +
            (notes.Count > 0 ? "; " + string.Join("; ", notes) : ""), "system (housekeeping)");
        return new HousekeepingResult(read, unread, notes);
    }

    private int DeleteInBatches(string where, string cut)
    {
        int total = 0, n;
        do
        {
            n = db.Exec($"DELETE FROM Notifications WHERE Id IN (SELECT Id FROM Notifications WHERE {where} LIMIT {Batch})", new { cut });
            total += n;
        } while (n == Batch);
        return total;
    }
}
