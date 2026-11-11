namespace FleetOps.Modules.Alerts;

public sealed class A06MaintenanceEvaluator(IMaintenanceAlertFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-06";
    public string SubjectType => "vehicle";

    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var criticalDays = await context.Settings.GetIntAsync(new("alerts.a06.critical_days"), context.Command, cancellationToken);
        var criticalKm = await context.Settings.GetIntAsync(new("alerts.a06.critical_km"), context.Command, cancellationToken);
        var result = new List<AlertCondition>();
        foreach (var item in await facts.ReadAsync(subjectId, context.Command, cancellationToken))
        {
            var dateOverdue = item.DueDate < today;
            var fresh = item.KmAsOf.HasValue && today.DayNumber - DateOnly.FromDateTime(item.KmAsOf.Value.Date).DayNumber <= 30;
            var kmOverdue = fresh && item.DueKm.HasValue && item.OdometerKm >= item.DueKm;
            if (!dateOverdue && !kmOverdue) continue;
            var critical = dateOverdue && today.DayNumber - item.DueDate!.Value.DayNumber > criticalDays ||
                kmOverdue && (long)item.OdometerKm!.Value - item.DueKm!.Value > criticalKm;
            result.Add(new(item.Id.ToString(), critical ? AlertSeverity.Critical : AlertSeverity.Warning, item.DueDate));
        }
        return result;
    }
}
