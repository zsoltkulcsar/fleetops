using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record DefectOriginCounts(int App, int OfficeFromChat);
