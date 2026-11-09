using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Schedule;

public sealed class EditCycleHandler(IScheduleEditor editor, IScheduleChanged changed) : ICommandHandler<EditCycleCommand, CycleDetail>
{
    public async Task<CycleDetail> HandleAsync(EditCycleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        ManualCycleRules.Validate(command.EmployeeId, command.VehicleId, command.From, command.To, command.Reason);
        if (command.Id == Guid.Empty || command.Version is < 1 or int.MaxValue) throw new ScheduleValidationException(["version"]);
        var mutation = await editor.EditAsync(command, actor, context, cancellationToken);
        await changed.NotifyAsync(mutation, context, cancellationToken);
        return mutation.Detail;
    }
}
