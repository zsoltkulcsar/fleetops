using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("maintenance.manage")]
public sealed record CreateRepairCommand(Guid VehicleId, string? Workshop, DateOnly ScheduledFor, Guid[]? DefectReportIds = null, string Type = "repair", DateOnly? DueDate = null, int? DueKm = null) : ICommand<RepairDetail>;
