using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[QueryPolicy("absences.view")]
public sealed record GetAbsenceQuery(Guid Id) : IQuery<AbsenceDetail>;
