using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
using FleetOps.Modules.Audit;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfComplianceRuleStore(IConfiguration configuration, TimeProvider clock) : IComplianceRuleStore
{
    private FleetOpsDbContext Open(CommandContext? context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        if (context is null) MigrationDbContextOptions.Configure(options, configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        else MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        if (context is not null) db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
    public async Task<IReadOnlyList<ComplianceRuleView>> ReadAsync(CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.ComplianceRules.AsNoTracking().OrderBy(rule => rule.Id)
            .Select(rule => new ComplianceRuleView(rule.Id, rule.Name, rule.GuideValue, rule.Value, rule.Confirmed) { ValueSchema = rule.ValueSchema }).ToArrayAsync(cancellationToken);
    }
    public async Task UpdateAsync(string ruleId, string? value, bool confirm, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(700079)", cancellationToken);
        var rule = await db.ComplianceRules.SingleOrDefaultAsync(rule => rule.Id == ruleId, cancellationToken) ?? throw new SettingsValidationException(["ruleId"]);
        var old = System.Text.Json.JsonSerializer.Serialize(new { rule.Value, rule.Confirmed });
        if (confirm)
        {
            if (rule.Value is null) throw new SettingsValidationException(["value"]);
            rule.Confirmed = true;
            db.RuleConfirmations.Add(new RuleConfirmation { Id = Guid.CreateVersion7(), RuleId = rule.Id, Value = rule.Value, ConfirmedBy = actor.EmployeeId, ConfirmedAt = clock.GetUtcNow() });
        }
        else
        {
            rule.Value = value;
            rule.Confirmed = false;
        }
        db.AuditEntries.Add(new AuditEntry { Id = Guid.CreateVersion7(), EntityType = "compliance_rule", EntityId = Guid.Empty,
            FieldName = rule.Id, OldValue = old, NewValue = System.Text.Json.JsonSerializer.Serialize(new { rule.Value, rule.Confirmed }),
            ActorId = actor.EmployeeId.ToString(), Reason = confirm ? "rule_confirmed" : "rule_edited", CreatedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }
}
