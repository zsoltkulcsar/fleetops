using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public sealed record ReleaseReviewRecipient(Guid Id, IReadOnlySet<RoleCode> Roles);
