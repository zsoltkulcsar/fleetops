using FleetOps.Modules.Audit;
namespace FleetOps.Modules.Maintenance;

public enum MaintenanceStatus { Scheduled, InProgress, Done, Cancelled }

[AuditedEntity]
public sealed class Maintenance
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public string Type { get; set; } = "repair";
    [AuditField(AuditFieldClasses.Plain)] public Guid VehicleId { get; set; }
    [AuditField(AuditFieldClasses.Personal)] public string Workshop { get; set; } = "";
    [AuditField(AuditFieldClasses.Plain)] public DateOnly ScheduledFor { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid[] DefectReportIds { get; set; } = [];
    [AuditField(AuditFieldClasses.Plain)] public MaintenanceStatus Status { get; set; }
    [AuditField(AuditFieldClasses.Personal)] public string? CancelReason { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly? DoneAt { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public int? OdometerAtDoneKm { get; set; }
}
