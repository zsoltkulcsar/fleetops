using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[QueryPolicy("absences.preview")]
public sealed record PreviewAbsenceQuery(Guid? EmployeeId, DateOnly From, DateOnly To, HalfDay? HalfDay, AbsenceType Type) : IQuery<AbsencePreview>, IHasSubject
{
    public Guid? SubjectId => EmployeeId;
}
