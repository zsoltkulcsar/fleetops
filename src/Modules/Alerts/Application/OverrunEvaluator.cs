using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Alerts;

public interface ICycleOverrunFacts
{
    Task<bool> IsOverrunAsync(Guid cycleId, CommandContext context, CancellationToken cancellationToken);
}

public sealed class A20OverrunEvaluator(ICycleOverrunFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-20";
    public string SubjectType => "cycle";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken) =>
        await facts.IsOverrunAsync(subjectId, context.Command, cancellationToken) ? [new("overrun", AlertSeverity.Warning, today)] : [];
}
