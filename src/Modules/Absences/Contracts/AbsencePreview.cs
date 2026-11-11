using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AbsencePreview(decimal CountedDays, decimal? BalanceBefore, decimal? BalanceAfter, bool? OverBalance, int OthersAbsentCount) : IReadModel;
