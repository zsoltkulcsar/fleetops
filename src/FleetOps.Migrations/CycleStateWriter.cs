using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfCycleStateWriter(IChangedSubjects changed) : ICycleStateWriter
{
    public async Task SaveAsync(Cycle cycle, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        await using var db = new FleetOpsDbContext(options.Options) { AuditActorId = actorId, AuditReason = "cycle_completion" };
        db.Database.UseTransaction(context.DbTransaction);
        var current = await db.Cycles.SingleAsync(row => row.Id == cycle.Id, cancellationToken);
        db.Entry(current).CurrentValues.SetValues(cycle);
        await db.SaveChangesAsync(cancellationToken);
        changed.Add("cycle", cycle.Id);
    }
}
