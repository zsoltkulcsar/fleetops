using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[CommandPolicy("maintenance.manage")]
public sealed record DoneRepairCommand(Guid Id, DateOnly? DoneAt = null, int? OdometerAtDoneKm = null, decimal? CostEur = null, Guid? InvoiceDocumentId = null, MaintenanceResultDocument? ResultDocument = null) : ICommand<RepairDetail>;
