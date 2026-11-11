using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public sealed class RuleWarningAlerts(IRuleWarningAlertStore store, TimeProvider clock) : IRuleWarningAlerts
{
    public async Task UpdateAsync(Guid shiftId, IReadOnlyList<string> ruleIds, bool confirmed, bool newWarning, CommandContext context, CancellationToken cancellationToken)
    {
        var alert = await store.FindAsync(shiftId, context, cancellationToken);
        if (alert is null && ruleIds.Count == 0) return;
        alert ??= new Alert { RuleId = "A-11", SubjectType = "shift", SubjectId = shiftId, AssignedRole = "dispatcher", OpenedAt = clock.GetUtcNow() };
        alert.Severity = confirmed ? AlertSeverity.Warning : AlertSeverity.Info;
        if (ruleIds.Count == 0)
        {
            alert.Status = AlertStatus.Resolved;
            alert.ResolvedAt = clock.GetUtcNow();
        }
        else if (newWarning)
        {
            alert.Status = AlertStatus.Open;
            alert.ResolvedAt = null;
            alert.ResolvedBy = null;
            alert.SnoozedUntil = null;
        }
        alert.Version++;
        await store.SaveAsync(alert, null, null, context, cancellationToken);
    }
}
