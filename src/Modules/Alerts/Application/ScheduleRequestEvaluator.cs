namespace FleetOps.Modules.Alerts;
public sealed class A19ScheduleRequestEvaluator(IPlanningAlertFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-19";
    public string SubjectType => "schedule_request";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken) =>
        await facts.OpenScheduleRequestAsync(subjectId, context.Command, cancellationToken) ? [new("open", AlertSeverity.Warning, today)] : [];
}
