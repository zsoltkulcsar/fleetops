namespace FleetOps.Modules.Maintenance;
public interface IRepairReads
{
    Task<MaintenanceList> ListAsync(ListMaintenanceQuery query, CancellationToken cancellationToken);
    Task<SwapOptions> OptionsAsync(SwapOptionsQuery query, CancellationToken cancellationToken);
}
