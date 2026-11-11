using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("defects.manage")]
public sealed record StartRepairCommand(Guid Id) : ICommand<RepairDetail>;
