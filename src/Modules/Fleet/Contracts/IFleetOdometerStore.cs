using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;

public interface IFleetOdometerStore
{
    Task<Vehicle?> LockAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(Vehicle vehicle, Guid actorId, CommandContext context, CancellationToken cancellationToken);
}
