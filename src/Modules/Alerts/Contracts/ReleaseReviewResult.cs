using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record ReleaseReviewResult(Guid AlertId, string Status, int Version) : IReadModel;
