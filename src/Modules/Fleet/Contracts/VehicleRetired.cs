using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

public sealed record VehicleRetired(Guid VehicleId);
public interface IVehicleRetiredEffects
{
    Task HandleAsync(VehicleRetired notification, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
