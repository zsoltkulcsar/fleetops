using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ReleaseBanner(Guid OverrideId, string ReleasedBy, DateTimeOffset ValidUntil, string Text);
