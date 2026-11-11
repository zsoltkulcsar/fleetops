using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Alerts;

public sealed record CycleClashFacts(Guid VehicleId, DateOnly From, DateOnly To, bool Open, bool Overlaps)
{
    public IReadOnlyList<DateOnly> MaintenanceDates { get; init; } = [];
}

public interface ICycleClashFacts
{
    Task<CycleClashFacts?> ReadAsync(Guid cycleId, CommandContext context, CancellationToken cancellationToken);
    Task<bool> VehicleUsableAsync(Guid vehicleId, DateOnly date, CommandContext context, CancellationToken cancellationToken);
}

public sealed class A21CycleEvaluator(ICycleClashFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-21";
    public string SubjectType => "cycle";

    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var cycle = await facts.ReadAsync(subjectId, context.Command, cancellationToken);
        if (cycle is null || !cycle.Open) return [];
        var clashes = cycle.Overlaps || cycle.MaintenanceDates.Any(date => date >= cycle.From &&
            date.DayNumber - cycle.From.DayNumber < 7 && date <= cycle.To);
        for (var day = 0; !clashes && day < 7 && cycle.From.AddDays(day) <= cycle.To; day++)
            clashes = !await facts.VehicleUsableAsync(cycle.VehicleId, cycle.From.AddDays(day), context.Command, cancellationToken);
        return clashes ? [new AlertCondition("clash", today >= cycle.From ? AlertSeverity.Critical : AlertSeverity.Warning, cycle.From)] : [];
    }
}
