using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("defects.manage")]
public sealed record EditAssignmentCommand(Guid Id, DateOnly From, DateOnly? To) : ICommand<AssignmentDetail>;
