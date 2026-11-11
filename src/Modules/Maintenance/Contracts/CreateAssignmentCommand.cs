using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("defects.manage")]
public sealed record CreateAssignmentCommand(Guid EmployeeId, Guid VehicleId, DateOnly? To = null, Guid? MaintenanceId = null) : ICommand<AssignmentDetail>;
