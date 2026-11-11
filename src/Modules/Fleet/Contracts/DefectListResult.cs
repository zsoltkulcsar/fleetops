using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record DefectListResult(IReadOnlyList<DefectRow> Items, int Page, int PageSize, int Total, DefectOriginCounts OpenByOrigin) : IReadModel;
