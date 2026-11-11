using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;

public interface IFleetOdometer
{
    Task<bool> RecordAsync(Guid vehicleId, int km, DateTimeOffset at, Guid actorId, CommandContext context, CancellationToken cancellationToken);
}
