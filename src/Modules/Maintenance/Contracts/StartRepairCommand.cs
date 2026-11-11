using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("maintenance.manage")]
public sealed record StartRepairCommand(Guid Id) : ICommand<RepairDetail>;
