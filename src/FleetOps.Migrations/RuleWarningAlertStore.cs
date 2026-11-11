using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Audit;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfRuleWarningAlertStore(TimeProvider clock) : IRuleWarningAlertStore
{
    private static FleetOpsDbContext Open(CommandContext context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
    public async Task<Alert?> FindAsync(Guid shiftId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var key = "rule-warning:" + shiftId;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        return await db.Alerts.FromSqlInterpolated($"SELECT * FROM fleetops.alerts WHERE rule_id = 'A-11' AND subject_id = {shiftId} AND subject_type = 'shift' ORDER BY opened_at DESC LIMIT 1 FOR UPDATE")
            .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }
    public async Task SaveAsync(Alert alert, string? note, Guid? actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        if (await db.Alerts.AnyAsync(row => row.Id == alert.Id, cancellationToken)) db.Alerts.Update(alert);
        else db.Alerts.Add(alert);
        if (note is not null) db.AuditEntries.Add(new AuditEntry { Id = Guid.CreateVersion7(), EntityType = "Alert", EntityId = alert.Id,
            FieldName = "Status", OldValue = "Open", NewValue = "Resolved", ActorId = actorId.ToString()!, Reason = note, CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }
}
