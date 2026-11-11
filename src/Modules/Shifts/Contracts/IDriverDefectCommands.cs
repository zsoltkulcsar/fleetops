using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Shifts;

public interface IDriverDefectCommands
{
    Task<DriverCommandResult> ApplyAsync(DriverCommand command, AccessContext actor, CommandContext context, DateTimeOffset receivedAt, CancellationToken cancellationToken);
}

