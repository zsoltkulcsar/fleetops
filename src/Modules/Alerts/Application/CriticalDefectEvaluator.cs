using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public sealed class A07CriticalDefectEvaluator(ICriticalDefectFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-07";
    public string SubjectType => "defect";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
        => await facts.IsOpenCriticalAsync(subjectId, context.Command, cancellationToken) ? [new("", AlertSeverity.Critical, null)] : [];
}
