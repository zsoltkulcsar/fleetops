using FleetOps.Api.Pipeline;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetOps.Api.Jobs;

public sealed class CycleDayJob(IConfiguration configuration, ICompanyTimeService companyTime, ICycleStateWriter writer, IAlertEngine alerts) : IJob
{
    public string Name => "cycles-day";
    public async Task<JobStep> RunStepAsync(JobContext job, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured."));
        await connection.OpenAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(connection).Options;
        await using var db = new FleetOpsDbContext(options);
        var today = companyTime.ToCompanyDate(job.Now);
        var after = job.Cursor is null ? Guid.Empty : Guid.Parse(job.Cursor);
        var ids = await db.Cycles.AsNoTracking().Where(cycle => cycle.Id.CompareTo(after) > 0 && cycle.From <= today &&
            (cycle.Status == CycleStatus.Planned || cycle.Status == CycleStatus.Active && cycle.To < today))
            .OrderBy(cycle => cycle.Id).Select(cycle => cycle.Id).Take(50).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var context = new CommandContext(connection, transaction);
            db.Database.UseTransaction(transaction);
            await EfCycleStore.LockScheduleAsync(context, cancellationToken);
            var cycle = await db.Cycles.AsNoTracking().SingleAsync(cycle => cycle.Id == id, cancellationToken);
            var changed = CycleCompletion.MarkOverrun(cycle, today);
            if (!changed && cycle.Status == CycleStatus.Planned && cycle.From <= today && cycle.To >= today)
            {
                cycle.Status = CycleStatus.Active;
                cycle.Version++;
                changed = true;
            }
            if (changed)
            {
                await writer.SaveAsync(cycle, CycleRolloverJob.ActorId, context, cancellationToken);
                await alerts.ApplyAsync("cycle", id, context, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            db.Database.UseTransaction(null);
        }
        return new(ids.Count < 50, ids.Count < 50 ? null : ids[^1].ToString(), ids.Count);
    }
}
