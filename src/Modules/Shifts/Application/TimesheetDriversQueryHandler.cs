using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Shifts;

public sealed class TimesheetDriversQueryHandler(ITimesheetDriverStore store) : IQueryHandler<GetTimesheetDriversQuery, TimesheetDrivers>
{
    public Task<TimesheetDrivers> HandleAsync(GetTimesheetDriversQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.Search?.Length > 200) throw new TimesheetValidationException("search");
        return store.FindAsync(query.Search?.Trim(), query.Id, cancellationToken);
    }
}
