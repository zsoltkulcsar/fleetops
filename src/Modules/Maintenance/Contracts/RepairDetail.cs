using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record RepairDetail(Guid Id, string Type, Guid VehicleId, string Workshop, DateOnly ScheduledFor, IReadOnlyList<Guid> DefectReportIds, MaintenanceStatus Status, string? CancelReason, DateOnly? DoneAt, int? OdometerAtDoneKm, bool InWorkshop) : IReadModel;
