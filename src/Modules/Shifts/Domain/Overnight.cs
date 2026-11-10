using FleetOps.Modules.Audit;

namespace FleetOps.Modules.Shifts;

public enum OvernightKind { Cab, Hotel }

[AuditedEntity]
public sealed class Overnight
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public Guid ShiftId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly Night { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public string CountryCode { get; set; } = "";
    [AuditField(AuditFieldClasses.Plain)] public OvernightKind Kind { get; set; }
}
