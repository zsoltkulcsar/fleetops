using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record PersonSession(Guid Id, string Device, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, string Status, string? RevokeReason, DateTimeOffset? PinResetRequestedAt);
