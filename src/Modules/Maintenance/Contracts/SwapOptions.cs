using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record SwapOptions(IReadOnlyList<SwapOption> Items) : IReadModel;
