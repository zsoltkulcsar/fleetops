using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Shifts;

[QueryPolicy("shifts.approve_reject")]
public sealed record GetTimesheetDriversQuery(string? Search, Guid? Id) : IQuery<TimesheetDrivers>;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record TimesheetDrivers(IReadOnlyList<TimesheetDriver> Items) : IReadModel;

[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record TimesheetDriver(Guid Id, string Name, string? PersonnelNumber);

public interface ITimesheetDriverStore
{
    Task<TimesheetDrivers> FindAsync(string? search, Guid? id, CancellationToken cancellationToken);
}
