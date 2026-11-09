using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record CyclePattern(int OnWeeks, int OffWeeks, DateOnly AnchorDate, Guid VehicleId);
