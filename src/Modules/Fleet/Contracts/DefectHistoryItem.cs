using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Owner)]
public sealed record DefectHistoryItem(string FieldName, string? OldValue, string? NewValue, string ActorId, string Reason, DateTimeOffset At);
