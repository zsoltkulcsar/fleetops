using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record MaintenanceList(IReadOnlyList<RepairDetail> Items) : IReadModel;
