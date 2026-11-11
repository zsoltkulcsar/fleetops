using FleetOps.Modules.Maintenance;
namespace FleetOps.Api;
public sealed record DoneRepairRequest(DateOnly? DoneAt = null, int? OdometerAtDoneKm = null, decimal? CostEur = null, Guid? InvoiceDocumentId = null, MaintenanceResultDocument? ResultDocument = null);
