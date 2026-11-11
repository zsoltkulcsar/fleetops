using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("defects.manage")]
public sealed record EndAssignmentCommand(Guid Id) : ICommand<AssignmentDetail>;
