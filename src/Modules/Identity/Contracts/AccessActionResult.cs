using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AccessActionResult(string Code) : IReadModel;
