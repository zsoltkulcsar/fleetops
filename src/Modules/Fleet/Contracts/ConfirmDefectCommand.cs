using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[CommandPolicy("defects.manage", HasStatePrecondition = true)]
public sealed record ConfirmDefectCommand(Guid Id) : ICommand<DefectActionResult>;
