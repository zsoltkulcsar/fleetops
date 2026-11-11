using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;
public interface IVehicleReleaseReview
{
    Task OpenAsync(VehicleOverride release, bool notifyOwner, CommandContext context, CancellationToken cancellationToken);
}
