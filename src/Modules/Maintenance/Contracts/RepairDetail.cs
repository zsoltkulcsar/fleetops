using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record RepairDetail(Guid Id, string Type, Guid VehicleId, string Workshop, DateOnly ScheduledFor, IReadOnlyList<Guid> DefectReportIds, MaintenanceStatus Status, string? CancelReason, DateOnly? DoneAt, int? OdometerAtDoneKm, bool InWorkshop) : IReadModel
{
    public DateOnly? DueDate { get; init; }
    public int? DueKm { get; init; }
    public decimal? CostEur { get; init; }
    public Guid? InvoiceDocumentId { get; init; }
    public Guid? ResultDocumentId { get; init; }
    public string? Plate { get; init; }
    public int? OdometerKm { get; init; }
    public DateTimeOffset? KmAsOf { get; init; }
    public bool KmReadingStale { get; init; }
    public string? KmNote => KmReadingStale ? "km reading older than 30 days" : null;
    public bool Overdue { get; init; }
    public NextServiceProposal? NextService { get; init; }
}
