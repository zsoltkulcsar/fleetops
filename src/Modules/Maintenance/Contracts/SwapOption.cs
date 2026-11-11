using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record SwapOption(Guid VehicleId, string Plate, string? BelongsTo, string? RunningShiftUserName, DateTimeOffset? RunningShiftStartAt) : IReadModel;
