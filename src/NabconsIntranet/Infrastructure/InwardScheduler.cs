using NabconsIntranet.Data;

namespace NabconsIntranet.Infrastructure;

/// <summary>
/// Runs the inward reminders once per office day, at or after Inward:ReminderHour (DD-08 §8). The last run date is kept
/// in the database (InwardState), so a restart never sends them twice.
/// </summary>
public sealed class InwardScheduler(InwardWorkflow workflow, InwardOptions o, AuditService audit, ILogger<InwardScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!o.Enabled) return;
        try { await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = workflow.RunDailyIfDue();
                if (result != null)
                    audit.Log("Inward.Reminders", "System", null, result.Count == 0 ? "No reminders due" : string.Join("; ", result), "system (inward reminders)");
            }
            catch (Exception ex) { log.LogError(ex, "Inward reminders failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
