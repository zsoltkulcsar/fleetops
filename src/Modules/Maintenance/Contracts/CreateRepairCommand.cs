using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("defects.manage")]
public sealed record CreateRepairCommand(Guid VehicleId, string? Workshop, DateOnly ScheduledFor, Guid[]? DefectReportIds) : ICommand<RepairDetail>;
