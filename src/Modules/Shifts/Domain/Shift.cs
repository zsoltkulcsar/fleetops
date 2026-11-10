using FleetOps.Modules.Audit;

namespace FleetOps.Modules.Shifts;

public enum ShiftStatus { Running, Draft, Submitted, Approved, Rejected, Exported }
public enum ShiftSource { App, Office }

[AuditedEntity]
public sealed class Shift
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public Guid EmployeeId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly Date { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset StartAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset? EndAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid? TruckId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid? TrailerId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public int? OdometerStartKm { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public int? OdometerEndKm { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public bool? DepartureCheckOk { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public ShiftSource Source { get; set; } = ShiftSource.App;
    [AuditField(AuditFieldClasses.Plain)] public ShiftStatus Status { get; set; } = ShiftStatus.Running;
    [AuditField(AuditFieldClasses.Personal)] public string? RejectReason { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset DeviceTime { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset ServerReceivedAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public bool DeviceTimeGap { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public string? BlockedAtStart { get; set; }
    [NotAudited] public int Version { get; set; } = 1;
    [NotAudited] public int LastOfficeVersion { get; set; }
    [NotAudited] public List<ShiftActivity> Activities { get; set; } = [];
    [NotAudited] public List<Overnight> Overnights { get; set; } = [];
}
