using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;

public sealed class EfFleetOdometerStore : IFleetOdometerStore
{
    public async Task<Vehicle?> LockAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        return await db.Vehicles.FromSqlInterpolated($"SELECT * FROM fleetops.vehicles WHERE id = {vehicleId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(Vehicle vehicle, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        db.AuditActorId = actorId;
        db.AuditReason = "odometer_recorded";
        var original = await db.Vehicles.SingleAsync(row => row.Id == vehicle.Id, cancellationToken);
        original.OdometerKm = vehicle.OdometerKm;
        original.OdometerUpdatedAt = vehicle.OdometerUpdatedAt;
        original.Version++;
        await db.SaveChangesAsync(cancellationToken);
    }
}
