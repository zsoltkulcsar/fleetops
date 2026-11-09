using FleetOps.Kernel.Authorization;
using FleetOps.Kernel;

namespace FleetOps.Modules.Readiness;

public interface IReadiness
{
    Task<IReadOnlyDictionary<Guid, VehicleReadiness>> ForVehiclesAsync(IReadOnlyCollection<Guid> ids, DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, DriverReadiness>> ForDriversAsync(IReadOnlyCollection<Guid> ids, DateOnly date, CancellationToken cancellationToken);
}

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ReadinessSummary(int VehiclesUsable, int VehiclesTotal, int DriversAbleToDrive, int DriversTotal) : IReadModel;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DriverReadinessItem(Guid Id, string Name, bool CanDrive, IReadOnlyList<ReadinessCause> Causes, bool Enrolled = false);

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record DriverReadinessList(IReadOnlyList<DriverReadinessItem> Items, string? NextCursor) : IReadModel;

[QueryPolicy("readiness.summary")]
public sealed record GetReadinessSummaryQuery : IQuery<ReadinessSummary>;

[QueryPolicy("employees.view")]
public sealed record ListDriverReadinessQuery(string? Cursor, int Limit, bool BlockedOnly) : IQuery<DriverReadinessList>;
