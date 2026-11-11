using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

public sealed record ReportDefectPayload(Guid VehicleId, DefectCategory Category, DefectSeverity Severity, string? Description = null, Guid[]? Photos = null);
public sealed record CancelDefectPayload(Guid? DefectId = null);

public interface IDefectStore
{
    Task LockVehicleAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken);
    Task<DefectReport?> LoadAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task ValidateReportAsync(DefectReport report, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(DefectReport report, Guid actorId, CommandContext context, CancellationToken cancellationToken);
    Task<bool> IsOffRoadAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken);
}

public interface IDefectEvents
{
    Task ReportedAsync(DefectReport defect, bool becameOffRoad, CommandContext context, CancellationToken cancellationToken);
    Task CancelledAsync(DefectReport defect, CommandContext context, CancellationToken cancellationToken);
}
