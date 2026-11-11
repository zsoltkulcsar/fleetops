using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AbsenceDetail(AbsenceView Absence, string EmployeeName, decimal WorkingDays, IReadOnlyList<VacationBalance> BalancesBefore, IReadOnlyList<VacationBalance> BalancesAfter, int OthersAbsentCount, IReadOnlyList<string> OthersAbsentNames, string DayCounting) : IReadModel;
