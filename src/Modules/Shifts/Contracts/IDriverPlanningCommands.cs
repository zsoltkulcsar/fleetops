using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Shifts;
public interface IDriverPlanningCommands
{
    Task<DriverCommandResult> ApplyAsync(DriverCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
