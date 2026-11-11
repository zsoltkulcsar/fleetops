using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Schedule;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record ScheduleSwap(Guid Id, Guid EmployeeId, string EmployeeName, Guid VehicleId, string VehiclePlate,
    DateOnly From, DateOnly? To, Guid? MaintenanceId, string Label = "Swap");
