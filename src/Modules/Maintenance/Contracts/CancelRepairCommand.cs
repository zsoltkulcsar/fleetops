using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("defects.manage")]
public sealed record CancelRepairCommand(Guid Id, string? Reason) : ICommand<RepairDetail>;
