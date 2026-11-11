using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[QueryPolicy("defects.manage")]
public sealed record SwapOptionsQuery(Guid EmployeeId, DateOnly Date) : IQuery<SwapOptions>;
