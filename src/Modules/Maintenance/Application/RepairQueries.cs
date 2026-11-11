using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Maintenance;

public sealed class RepairQueries(IRepairReads reads, IRepairStore store) :
    IQueryHandler<ListMaintenanceQuery, MaintenanceList>, IQueryHandler<SwapOptionsQuery, SwapOptions>
{
    public Task<MaintenanceList> HandleAsync(ListMaintenanceQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.StatusFilter is not null)
        {
            query = query.StatusFilter.ToLowerInvariant() switch
            {
                "scheduled" => query with { Status = MaintenanceStatus.Scheduled },
                "in_progress" or "inprogress" or "in_workshop" => query with { Status = MaintenanceStatus.InProgress },
                "done" => query with { Status = MaintenanceStatus.Done },
                "cancelled" => query with { Status = MaintenanceStatus.Cancelled },
                "overdue" => query with { Overdue = true },
                _ => throw new VehicleValidationException(["status"])
            };
        }
        if (query.VehicleId == Guid.Empty) throw new VehicleValidationException(["vehicleId"]);
        if (query.Status.HasValue && !Enum.IsDefined(query.Status.Value)) throw new VehicleValidationException(["status"]);
        if (query.From == default(DateOnly) || query.To == default(DateOnly) || query.To < query.From)
            throw new VehicleValidationException(["from", "to"]);
        if (query.Type is not null && !MaintenanceRules.Types.Contains(query.Type)) throw new VehicleValidationException(["type"]);
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
