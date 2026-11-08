using NabconsIntranet.Data;

namespace NabconsIntranet.Infrastructure;

/// <summary>
/// Runs the compliance reminders once per office day, at or after Compliance:ReminderHour, whenever the server is on
/// (DD-07 §8). Independent of the backup time, so a PC switched off in the evening still sends reminders next morning.
/// The last run date is kept in the database (ComplianceState), so a restart never sends them twice.
/// </summary>
public sealed class ComplianceScheduler(ComplianceWorkflow workflow, ComplianceOptions o, AuditService audit, ILogger<ComplianceScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!o.Enabled) return;
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = workflow.RunDailyIfDue();
                if (result != null)
                    audit.Log("Compliance.Reminders", "System", null, result.Count == 0 ? "No reminders due" : string.Join("; ", result), "system (compliance reminders)");
            }
            catch (Exception ex) { log.LogError(ex, "Compliance reminders failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
