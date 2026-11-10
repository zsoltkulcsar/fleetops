using FleetOps.Modules.Audit;

namespace FleetOps.Modules.Absences;

public enum AbsenceType { Vacation, Sick, Training, Unpaid, TimeOffInLieu, Other }
public enum AbsenceStatus { Requested, Approved, Declined }
public enum HalfDay { Am, Pm }

[AuditedEntity]
public sealed class Absence
{
    [NotAudited] public Guid Id { get; set; } = Guid.CreateVersion7();
    [AuditField(AuditFieldClasses.Plain)] public Guid EmployeeId { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly From { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public DateOnly To { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public HalfDay? HalfDay { get; set; }
    [AuditField(AuditFieldClasses.Personal)] public AbsenceType Type { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public AbsenceStatus Status { get; set; }
    [AuditField(AuditFieldClasses.Plain)] public Guid? DecidedBy { get; set; }
}
