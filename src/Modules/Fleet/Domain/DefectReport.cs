using FleetOps.Modules.Audit;

namespace FleetOps.Modules.Fleet;

public enum DefectCategory { Brakes, Lights, Tyres, Engine, Body, Tacho, LoadSecuring, Other }
public enum DefectSeverity { Minor, Major, Critical }
public enum DefectOrigin { App, OfficeFromChat }
public enum DefectStatus { Open, Acknowledged, InRepair, Fixed, Closed, Cancelled }

[AuditedEntity]
public sealed class DefectReport
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public Guid VehicleId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid ReportedBy { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset ReportedAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DefectOrigin Origin { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DefectCategory Category { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DefectSeverity Severity { get; set; }
    [AuditField(AuditFieldClasses.Personal)] public string? Description { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid[] Photos { get; set; } = [];
    [AuditField(AuditFieldClasses.Plain)] public DefectStatus Status { get; set; } = DefectStatus.Open;
    [AuditField(AuditFieldClasses.Plain)] public Guid? ClosedBy { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateTimeOffset? ClosedAt { get; set; }
    [AuditField(AuditFieldClasses.Personal)] public string? CloseReason { get; set; }
    [NotAudited] public DateTimeOffset DeviceTime { get; set; }
    [NotAudited] public DateTimeOffset ServerReceivedAt { get; set; }
    [NotAudited] public Guid? CommandId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid? RepairId { get; set; }
}
