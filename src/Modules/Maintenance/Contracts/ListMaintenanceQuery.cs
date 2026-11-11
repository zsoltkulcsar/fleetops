using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[QueryPolicy("maintenance.manage")]
public sealed record ListMaintenanceQuery(Guid? VehicleId = null, MaintenanceStatus? Status = null, DateOnly? From = null, DateOnly? To = null, string? Type = null, bool? Overdue = null, string? StatusFilter = null) : IQuery<MaintenanceList>;
