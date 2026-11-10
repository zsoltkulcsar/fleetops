using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;

namespace FleetOps.Modules.Alerts;

public sealed record TimesheetAlertFacts(string Status, DateTimeOffset StartAt, DateTimeOffset? SubmittedAt);
public interface ITimesheetAlertFacts
{
    Task<TimesheetAlertFacts?> ReadAsync(Guid shiftId, CommandContext context, CancellationToken cancellationToken);
}

public sealed class A10ApprovalEvaluator(ITimesheetAlertFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-10";
    public string SubjectType => "shift";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var shift = await facts.ReadAsync(subjectId, context.Command, cancellationToken);
        if (shift is null || shift.Status != "Submitted") return [];
        var day = await context.Settings.GetIntAsync(new("company.payroll_cutoff_day"), context.Command, cancellationToken);
        var cutoff = PayrollCutoff.Next(today, day);
        return shift.SubmittedAt < now.AddDays(-3) || cutoff.DayNumber - today.DayNumber <= 2
            ? [new("", AlertSeverity.Warning, cutoff)] : [];
    }
}

public sealed class A16RunningShiftEvaluator(ITimesheetAlertFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-16";
    public string SubjectType => "shift";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var limit = await context.Settings.GetIntAsync(new("company.max_shift_hours"), context.Command, cancellationToken);
        if (limit <= 0) return [];
        var shift = await facts.ReadAsync(subjectId, context.Command, cancellationToken);
        return shift is { Status: "Running" } && now - shift.StartAt > TimeSpan.FromHours(limit)
            ? [new("", AlertSeverity.Warning, today)] : [];
    }
}
