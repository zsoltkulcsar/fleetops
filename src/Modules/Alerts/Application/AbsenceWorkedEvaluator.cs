namespace FleetOps.Modules.Alerts;
public sealed class A18AbsenceWorkedEvaluator(IPlanningAlertFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-18";
    public string SubjectType => "shift";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken) =>
        await facts.ShiftDuringAbsenceAsync(subjectId, context.Command, cancellationToken) ? [new("absence_worked", AlertSeverity.Warning, today)] : [];
}
