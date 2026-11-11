using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record VehicleReleaseEligibility(bool Enabled, bool Allowed, IReadOnlyList<string> ExpiredTypes,
    IReadOnlyList<string> ReleasableTypes, ActiveVehicleRelease? ActiveOverride);
