using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.People;

namespace FleetOps.Modules.Schedule;

public interface IsInCycle
{
    Task<bool> ForEmployeeAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default);
    async Task<IReadOnlySet<DateOnly>> ForEmployeeDatesAsync(Guid employeeId, IReadOnlyCollection<DateOnly> dates, CancellationToken cancellationToken = default)
    {
        var result = new HashSet<DateOnly>();
        foreach (var date in dates)
            if (await ForEmployeeAsync(employeeId, date, cancellationToken)) result.Add(date);
        return result;
    }
}

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record CyclePatternResult(Guid EmployeeId, int Version, int Generated) : IReadModel;

[CommandPolicy("schedule.edit")]
public sealed record SaveCyclePatternCommand(Guid? SubjectId, int Version, CyclePattern Pattern) : ICommand<CyclePatternResult>, IHasSubject;

public sealed record CyclePatternChanged(Guid EmployeeId);

public interface ICycleStore
{
    Task SavePatternAsync(SaveCyclePatternCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<Cycle>> LoadCyclesAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken);
    Task AddCyclesAsync(IReadOnlyList<Cycle> cycles, CommandContext context, Guid actorId, CancellationToken cancellationToken);
    Task RemoveFutureGeneratedAsync(Guid employeeId, DateOnly today, CommandContext context, Guid actorId, CancellationToken cancellationToken);
}

public sealed class ScheduleValidationException(IReadOnlyList<string> fields) : Exception("validation_error")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}

public sealed class ScheduleConflictException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
