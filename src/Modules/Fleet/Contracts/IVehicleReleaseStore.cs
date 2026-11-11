using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;
public interface IVehicleReleaseStore
{
    Task<VehicleReleaseFacts?> LockFactsAsync(Guid vehicleId, DateOnly date, CommandContext context, CancellationToken cancellationToken);
    Task<string> ActorNameAsync(Guid actorId, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(VehicleOverride release, CommandContext context, CancellationToken cancellationToken);
}
