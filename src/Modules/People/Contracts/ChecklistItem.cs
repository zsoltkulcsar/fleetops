using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record ChecklistItem(string Type, string? State, bool Masked, DateOnly? ExpiresAt);
