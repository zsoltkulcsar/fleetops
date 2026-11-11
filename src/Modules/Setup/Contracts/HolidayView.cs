using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record HolidayView(DateOnly Date, string Name);
