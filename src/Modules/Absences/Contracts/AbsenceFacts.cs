using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
public sealed record AbsenceFacts(Guid EmployeeId, string EmployeeName, decimal? Entitlement, decimal? Opening, int? OpeningYear, IReadOnlyList<Absence> Absences, IReadOnlySet<DateOnly> Holidays);
