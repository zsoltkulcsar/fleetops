using FleetOps.Modules.Audit;

namespace FleetOps.Modules.Fleet;

[AuditedEntity]
public sealed class Assignment
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public Guid EmployeeId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid VehicleId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly From { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly? To { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid? MaintenanceId { get; set; }
    [AuditField(AuditFieldClasses.Personal)] public string? Note { get; set; }
}
