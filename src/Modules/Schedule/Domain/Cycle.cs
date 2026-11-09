using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Audit;

namespace FleetOps.Modules.Schedule;

public enum CycleStatus { Planned, Active, Overrun, Done, Cancelled }
public enum CycleSource { Generated, Manual }

[AuditedEntity]
public sealed class Cycle
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public Guid EmployeeId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid VehicleId { get; set; }
    // Retained when a dispatcher edits dates or truck; null for an independent stand-in.
    [AuditField(AuditFieldClasses.Plain)] public int? PatternSlot { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly From { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly To { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public CycleStatus Status { get; set; } = CycleStatus.Planned;
    [AuditField(AuditFieldClasses.Plain)] public CycleSource Source { get; set; } = CycleSource.Generated;
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset? FinishedAt { get; set; }
    [NotAudited] public int Version { get; set; } = 1;
}
