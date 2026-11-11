using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[QueryPolicy("defects.manage")]
public sealed record ListMaintenanceQuery(Guid? VehicleId = null, MaintenanceStatus? Status = null, DateOnly? From = null, DateOnly? To = null) : IQuery<MaintenanceList>;
