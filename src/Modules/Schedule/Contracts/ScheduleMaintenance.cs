using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Schedule;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record ScheduleMaintenance(Guid Id, Guid VehicleId, string VehiclePlate, string Type, DateOnly ScheduledFor, string Status, string Label) : IReadModel;
