using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class CyclePatternQueryHandler(FleetOpsDbContext db) : IQueryHandler<GetCyclePatternQuery, CyclePatternView>
{
    public async Task<CyclePatternView> HandleAsync(GetCyclePatternQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == query.EmployeeId, cancellationToken) ?? throw new ScheduleConflictException("driver_not_available");
        return new(employee.Id, employee.CyclePatternVersion, employee.CyclePattern);
    }
}

