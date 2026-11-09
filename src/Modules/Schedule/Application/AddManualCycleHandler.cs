using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Schedule;

public sealed class AddManualCycleHandler(IScheduleEditor editor, IScheduleChanged changed) : ICommandHandler<AddManualCycleCommand, CycleDetail>
{
    public async Task<CycleDetail> HandleAsync(AddManualCycleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        ManualCycleRules.Validate(command.EmployeeId, command.VehicleId, command.From, command.To, command.Reason);
        var mutation = await editor.AddAsync(command, actor, context, cancellationToken);
        await changed.NotifyAsync(mutation, context, cancellationToken);
        return mutation.Detail;
    }
}

