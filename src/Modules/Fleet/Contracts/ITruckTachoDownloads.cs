using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;

public interface ITruckTachoDownloads
{
    Task<Vehicle?> LockAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(Vehicle truck, Guid actorId, CommandContext context, CancellationToken cancellationToken);
}
