using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class ScheduleQueryHandler(FleetOpsDbContext db, TimeProvider time) : IQueryHandler<GetScheduleQuery, ScheduleResult>
{
    public async Task<ScheduleResult> HandleAsync(GetScheduleQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.From == default || query.To < query.From || query.To.DayNumber - query.From.DayNumber > 366) throw new ScheduleValidationException(["from", "to"]);
        var cycles = db.Cycles.Where(cycle => cycle.From <= query.To && cycle.To >= query.From);
        if (query.EmployeeId.HasValue) cycles = cycles.Where(cycle => cycle.EmployeeId == query.EmployeeId);
        if (query.VehicleId.HasValue) cycles = cycles.Where(cycle => cycle.VehicleId == query.VehicleId);
        return new(time.GetUtcNow(), await ScheduleProjection.ReadAsync(db, cycles, cancellationToken));
    }
}

