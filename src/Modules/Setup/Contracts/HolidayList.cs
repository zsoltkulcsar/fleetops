using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record HolidayList(IReadOnlyList<HolidayView> Items) : IReadModel;
