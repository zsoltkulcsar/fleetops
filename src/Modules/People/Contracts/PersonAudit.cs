using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed record PersonAudit(string Field, string? OldValue, string? NewValue, string? Reason = null);
