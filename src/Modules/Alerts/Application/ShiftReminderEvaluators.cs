using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Alerts;

public sealed record ShiftReminderFacts(bool InCycleToday, IReadOnlyList<DateOnly> MissingDays, bool PreviousWeekHasDrafts, DateOnly PreviousMonday);
public interface IShiftReminderFacts
{
    Task<ShiftReminderFacts> ReadAsync(Guid employeeId, DateOnly today, CommandContext context, CancellationToken cancellationToken);
}

public sealed class A09MissingShiftEvaluator(IShiftReminderFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-09";
    public string SubjectType => "employee";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var work = await facts.ReadAsync(subjectId, today, context.Command, cancellationToken);
        if (!work.InCycleToday) return [];
        return work.MissingDays.Select(date => new AlertCondition(date.ToString("yyyy-MM-dd"), AlertSeverity.Warning, date)).ToArray();
    }
}

public sealed class A15WeekSubmissionEvaluator(IShiftReminderFacts facts, ICompanyTimeService companyTime) : IAlertEvaluator
{
    public string RuleId => "A-15";
    public string SubjectType => "employee";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        if (today.DayOfWeek != DayOfWeek.Monday) return [];
        var hour = await context.Settings.GetIntAsync(new("timesheets.submit_reminder_hour"), context.Command, cancellationToken);
        if (companyTime.ToCompanyTime(now).Hour < hour) return [];
        var work = await facts.ReadAsync(subjectId, today, context.Command, cancellationToken);
        return work.InCycleToday && work.PreviousWeekHasDrafts ? [new(work.PreviousMonday.ToString("yyyy-MM-dd"), AlertSeverity.Warning, today)] : [];
    }
}
