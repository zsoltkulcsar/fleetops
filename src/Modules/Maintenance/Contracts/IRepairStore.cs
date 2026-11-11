using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Maintenance;

public interface IRepairStore
{
    Task<Vehicle?> LoadVehicleAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task ValidateInvoiceAsync(Guid documentId, Guid vehicleId, CommandContext context, CancellationToken cancellationToken);
    Task LockAsync(CommandContext context, CancellationToken cancellationToken);
    Task<Maintenance?> LoadRepairAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task SaveRepairAsync(Maintenance repair, Guid actorId, CommandContext context, CancellationToken cancellationToken);
    Task<Assignment?> LoadAssignmentAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task SaveAssignmentAsync(Assignment assignment, Guid actorId, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<Assignment>> LinkedAssignmentsAsync(Guid repairId, CommandContext context, CancellationToken cancellationToken);
    Task<bool> IsActiveDriverAsync(Guid id, DateOnly date, CommandContext? context, CancellationToken cancellationToken);
    Task<bool> IsUsableTruckAsync(Guid id, DateOnly date, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> CycleDriversAsync(Guid vehicleId, DateOnly from, DateOnly? to, CommandContext context, CancellationToken cancellationToken);
}
