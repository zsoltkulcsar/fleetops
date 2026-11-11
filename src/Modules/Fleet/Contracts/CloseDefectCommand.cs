using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[CommandPolicy("defects.manage", HasStatePrecondition = true)]
public sealed record CloseDefectCommand(Guid Id, string? Reason) : ICommand<DefectActionResult>;
