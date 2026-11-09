using FleetOps.Kernel.Authorization;
using FleetOps.People;

namespace FleetOps.Modules.Schedule;

public sealed class SaveCyclePatternHandler(ICycleStore store, CyclePatternChangedHandler changed, TimeProvider time, FleetOps.Kernel.ICompanyTimeService companyTime)
    : ICommandHandler<SaveCyclePatternCommand, CyclePatternResult>
{
    public async Task<CyclePatternResult> HandleAsync(SaveCyclePatternCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        CycleGeneration.Validate(command.Pattern);
        if (command.SubjectId is null || command.SubjectId == Guid.Empty) throw new ScheduleValidationException(["employeeId"]);
        if (command.Version is < 0 or int.MaxValue) throw new ScheduleValidationException(["version"]);
        await store.SavePatternAsync(command, actor, context, cancellationToken);
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var generated = await changed.HandleAsync(new(command.SubjectId.Value), command.Pattern, today, actor.EmployeeId, context, cancellationToken);
        return new(command.SubjectId.Value, command.Version + 1, generated);
    }
}

