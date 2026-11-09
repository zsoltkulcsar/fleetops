using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfIsInCycle(FleetOpsDbContext db) : IsInCycle
{
    public async Task<bool> ForEmployeeAsync(Guid employeeId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var cycles = await db.Cycles.AsNoTracking().Where(cycle => cycle.EmployeeId == employeeId && (cycle.Status == CycleStatus.Active || cycle.Status == CycleStatus.Overrun)).ToListAsync(cancellationToken);
        return cycles.Any(cycle => CycleGeneration.Contains(cycle, date));
    }
}
