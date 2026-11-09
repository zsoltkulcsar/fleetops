using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfCycleClashFacts : ICycleClashFacts
{
    public async Task<CycleClashFacts?> ReadAsync(Guid cycleId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        var cycle = await db.Cycles.AsNoTracking().SingleOrDefaultAsync(cycle => cycle.Id == cycleId, cancellationToken);
        if (cycle is null) return null;
        var open = cycle.Status is CycleStatus.Planned or CycleStatus.Active or CycleStatus.Overrun;
        var overlaps = open && await db.Cycles.AnyAsync(other => other.Id != cycleId && other.VehicleId == cycle.VehicleId &&
            (other.Status == CycleStatus.Planned || other.Status == CycleStatus.Active || other.Status == CycleStatus.Overrun) &&
            other.From < cycle.To && cycle.From < other.To, cancellationToken);
        return new(cycle.VehicleId, cycle.From, cycle.To, open, overlaps);
    }

    public async Task<bool> VehicleUsableAsync(Guid vehicleId, DateOnly date, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        var result = await new EfReadinessStore(db).ForVehiclesAsync([vehicleId], date, cancellationToken);
        return result.TryGetValue(vehicleId, out var readiness) && readiness.Usable;
    }
}
