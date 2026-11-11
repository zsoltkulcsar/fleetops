using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record VacationBalance(int Year, decimal Opening, decimal Entitlement, decimal Used, decimal Remaining) : IReadModel
{
    public decimal Left => Remaining;
}
