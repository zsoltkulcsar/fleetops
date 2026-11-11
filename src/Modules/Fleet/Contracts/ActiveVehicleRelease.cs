using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record ActiveVehicleRelease(string ByName, DateTimeOffset ValidUntil);
