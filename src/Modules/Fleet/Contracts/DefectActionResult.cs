using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record DefectActionResult(Guid Id, DefectStatus Status, DefectSeverity Severity) : IReadModel;
