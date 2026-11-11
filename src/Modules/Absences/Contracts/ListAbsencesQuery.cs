using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[QueryPolicy("absences.view")]
public sealed record ListAbsencesQuery(DateOnly From, DateOnly To, Guid? EmployeeId = null) : IQuery<AbsenceList>;
