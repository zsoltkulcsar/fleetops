using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Audit;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfReleaseReviewStore : IReleaseReviewStore
{
    private static FleetOpsDbContext Open(CommandContext context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
    public async Task AddAsync(Alert alert, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        db.Alerts.Add(alert);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<ReleaseReviewRecipient>> RecipientsAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var people = await db.Employees.AsNoTracking().Where(person => person.Status == FleetOps.People.EmployeeStatus.Active).Select(person => new { person.Id, person.Roles }).ToArrayAsync(cancellationToken);
        return people.Select(person => new ReleaseReviewRecipient(person.Id, (System.Text.Json.JsonSerializer.Deserialize<string[]>(person.Roles) ?? []).Select(role => role switch
        {
            "driver" => RoleCode.Driver, "dispatcher" => RoleCode.Dispatcher, "hr" => RoleCode.Hr, "owner" => RoleCode.Owner,
            _ => throw new InvalidOperationException("invalid_role")
        }).ToHashSet())).ToArray();
    }
    public async Task<Alert?> LockAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.Alerts.FromSqlInterpolated($"SELECT * FROM fleetops.alerts WHERE id = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }
    public async Task SaveAsync(Alert alert, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        db.Alerts.Update(alert);
        db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid(), EntityType = "alert", EntityId = alert.Id, FieldName = "status", OldValue = "open", NewValue = "resolved", ActorId = actor.EmployeeId.ToString(), Reason = alert.RuleId == "A-18" ? "absence_work_acknowledged" : "release_acknowledged", CreatedAt = alert.ResolvedAt!.Value });
        await db.SaveChangesAsync(cancellationToken);
    }
}
