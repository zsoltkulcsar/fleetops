using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record PersonHistoryEntry(Guid Id, DateTimeOffset CreatedAt, string Actor, string Field,
    string? OldValue, string? NewValue, string Reason, bool Masked);
