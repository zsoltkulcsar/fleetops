using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Maintenance;

public sealed class RepairQueries(IRepairReads reads, IRepairStore store) :
    IQueryHandler<ListMaintenanceQuery, MaintenanceList>, IQueryHandler<SwapOptionsQuery, SwapOptions>
{
    public Task<MaintenanceList> HandleAsync(ListMaintenanceQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.VehicleId == Guid.Empty) throw new VehicleValidationException(["vehicleId"]);
        if (query.Status.HasValue && !Enum.IsDefined(query.Status.Value)) throw new VehicleValidationException(["status"]);
        if (query.From == default(DateOnly) || query.To == default(DateOnly) || query.To < query.From)
            throw new VehicleValidationException(["from", "to"]);
        return reads.ListAsync(query, cancellationToken);
    }
    public async Task<SwapOptions> HandleAsync(SwapOptionsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.Date == default) throw new VehicleValidationException(["date"]);
        if (!await store.IsActiveDriverAsync(query.EmployeeId, query.Date, null, cancellationToken))
            throw new VehicleValidationException(["employeeId"]);
        return await reads.OptionsAsync(query, cancellationToken);
    }
}
