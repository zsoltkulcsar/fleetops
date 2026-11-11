using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record VehicleReleaseResult(Guid OverrideId, Guid VehicleId, Guid By, string ReleasedBy, DateTimeOffset At, DateTimeOffset ValidUntil, string Reason, IReadOnlyList<string> DocTypes) : IReadModel;
