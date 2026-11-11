using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Maintenance;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;

public sealed class EfMaintenanceAlertFacts : IMaintenanceAlertFacts
{
    public async Task<IReadOnlyList<MaintenanceDueFact>> ReadAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(vehicle => vehicle.Id == vehicleId && vehicle.Status == VehicleStatus.Active, cancellationToken);
        if (vehicle is null) return [];
        return await db.Maintenance.AsNoTracking().Where(item => item.VehicleId == vehicleId &&
            (item.Status == MaintenanceStatus.Scheduled || item.Status == MaintenanceStatus.InProgress))
            .Select(item => new MaintenanceDueFact(item.Id, item.DueDate, item.DueKm, vehicle.OdometerKm, vehicle.OdometerUpdatedAt)).ToListAsync(cancellationToken);
    }
}
