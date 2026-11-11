using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[QueryPolicy("defects.manage")]
public sealed record GetDefectQuery(Guid Id) : IQuery<DefectDetail>;
