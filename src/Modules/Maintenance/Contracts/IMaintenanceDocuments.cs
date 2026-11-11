using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

public interface IMaintenanceDocuments
{
    Task<Guid> CreateVerifiedResultAsync(Guid vehicleId, MaintenanceResultDocument input, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
