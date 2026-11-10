using FleetOps.Modules.Audit;

namespace FleetOps.Modules.Shifts;

public enum ActivityType { Driving, OtherWork, Availability, Break }

[AuditedEntity]
public sealed class ShiftActivity
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public Guid ShiftId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public ActivityType Type { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset StartAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset? EndAt { get; set; }
    [NotAudited] public DateTimeOffset RecordedAt { get; set; }
}
