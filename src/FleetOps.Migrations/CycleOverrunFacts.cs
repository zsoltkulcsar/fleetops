using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfCycleOverrunFacts : ICycleOverrunFacts
{
    public async Task<bool> IsOverrunAsync(Guid cycleId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        return await db.Cycles.AnyAsync(cycle => cycle.Id == cycleId && cycle.Status == CycleStatus.Overrun, cancellationToken);
    }
}
