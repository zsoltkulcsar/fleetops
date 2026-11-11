using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("maintenance.manage")]
public sealed record RescheduleMaintenanceCommand(Guid Id, DateOnly ScheduledFor, string? Workshop, DateOnly? DueDate, int? DueKm) : ICommand<RepairDetail>;
