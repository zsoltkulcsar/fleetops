using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;

public interface IMaintenanceAlertFacts
{
    Task<IReadOnlyList<MaintenanceDueFact>> ReadAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken);
}
