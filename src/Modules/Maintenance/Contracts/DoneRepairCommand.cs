using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("defects.manage")]
public sealed record DoneRepairCommand(Guid Id, DateOnly? DoneAt = null, int? OdometerAtDoneKm = null) : ICommand<RepairDetail>;
