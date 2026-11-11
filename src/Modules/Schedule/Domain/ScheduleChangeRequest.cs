using FleetOps.Modules.Audit;
namespace FleetOps.Modules.Schedule;
[AuditedEntity]
public sealed class ScheduleChangeRequest
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public Guid EmployeeId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid CycleId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly ProposedFrom { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly ProposedTo { get; set; }
    [AuditField(AuditFieldClasses.Personal)] public string Note { get; set; } = "";
    [AuditField(AuditFieldClasses.Plain)] public ScheduleRequestStatus Status { get; set; }
    [AuditField(AuditFieldClasses.Personal)] public string? DeclineReason { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid? DecidedBy { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset? DecidedAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid CommandId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset DeviceTime { get; set; }
}
