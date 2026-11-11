using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[CommandPolicy("defects.manage", HasStatePrecondition = true)]
public sealed record DowngradeDefectCommand(Guid Id, DefectSeverity Severity, string? Reason) : ICommand<DefectActionResult>;
