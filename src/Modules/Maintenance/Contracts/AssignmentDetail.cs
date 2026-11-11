using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record AssignmentDetail(Guid Id, Guid EmployeeId, Guid VehicleId, DateOnly From, DateOnly? To, Guid? MaintenanceId) : IReadModel;
