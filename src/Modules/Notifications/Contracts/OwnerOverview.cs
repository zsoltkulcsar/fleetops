using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Notifications;
[VisibleTo(RoleCode.Owner)]
public sealed record OwnerKpi(string Key, decimal? Value, string? Status, string Href);
[VisibleTo(RoleCode.Owner)]
public sealed record OwnerOverview(IReadOnlyList<OwnerKpi> Items) : IReadModel;
[QueryPolicy("owner.overview")]
public sealed record GetOwnerOverviewQuery : IQuery<OwnerOverview>;
public sealed record OwnerVehicleFact(Guid Id, VehicleKind Kind, int GrossWeightKg, bool OffRoad);
public sealed record OwnerDocumentFact(Guid OwnerId, bool Employee, string Type);
public sealed record OwnerOverviewFacts(int CriticalAlerts, IReadOnlyList<OwnerVehicleFact> Vehicles, IReadOnlyList<Guid> Drivers, IReadOnlyList<OwnerDocumentFact> CurrentDocuments, string? ExportStatus, decimal MaintenanceCostEur);
public interface IOwnerOverviewStore
{
    Task<OwnerOverviewFacts> ReadAsync(CancellationToken cancellationToken);
}
