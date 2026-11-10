using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Alerts;

public sealed record ShiftAlertFacts(IReadOnlyList<Guid> BlockedShifts, IReadOnlyList<Guid> Conflicts);
public interface IShiftAlertFacts
{
    Task<ShiftAlertFacts> ReadAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken);
}

public sealed class ShiftAwareEmployeeEvaluator(IAlertEvaluator documents, IShiftAlertFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-02";
    public string SubjectType => "employee";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var conditions = await documents.EvaluateAsync(subjectId, today, now, context, cancellationToken);
        var shifts = await facts.ReadAsync(subjectId, context.Command, cancellationToken);
        return conditions.Concat(shifts.BlockedShifts.Select(id => new AlertCondition($"blocked_shift:{id}", AlertSeverity.Critical, null))).ToArray();
    }
}

public sealed class A17ShiftConflictEvaluator(IShiftAlertFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-17";
    public string SubjectType => "employee";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var shifts = await facts.ReadAsync(subjectId, context.Command, cancellationToken);
        return shifts.Conflicts.Select(id => new AlertCondition($"shift_conflict:{id}", AlertSeverity.Warning, null)).ToArray();
    }
}
