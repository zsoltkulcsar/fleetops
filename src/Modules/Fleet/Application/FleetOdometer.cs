using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;

public sealed class FleetOdometer(IFleetOdometerStore store, IChangedSubjects changed) : IFleetOdometer
{
    public async Task<bool> RecordAsync(Guid vehicleId, int km, DateTimeOffset at, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        if (km < 0 || at == default) throw new VehicleValidationException(["odometerKm"]);
        var vehicle = await store.LockAsync(vehicleId, context, cancellationToken);
        if (vehicle is null || vehicle.Kind != VehicleKind.Truck) throw new VehicleValidationException(["vehicleId"]);
        if (vehicle.OdometerKm > km) return false;
        if (vehicle.OdometerKm == km && vehicle.OdometerUpdatedAt >= at) return true;
        vehicle.OdometerKm = km;
        vehicle.OdometerUpdatedAt = at;
        await store.SaveAsync(vehicle, actorId, context, cancellationToken);
        changed.Add("vehicle", vehicleId);
        return true;
    }
}
