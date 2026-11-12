using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
// Shared by A-13 (HR, owner policy) and A-11 (dispatcher policy) acknowledgements; each endpoint keeps its own policy.
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ReleaseReviewResult(Guid AlertId, string Status, int Version) : IReadModel;
