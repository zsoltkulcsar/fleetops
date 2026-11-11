using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[QueryPolicy("absences.balance")]
public sealed record GetVacationBalanceQuery(Guid EmployeeId, int Year) : IQuery<VacationBalance>;
