using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfShiftGuidanceStore(IRuleWarningAlerts alerts) : IShiftGuidanceStore
{
    private static FleetOpsDbContext Open(CommandContext context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
    public async Task<IReadOnlyList<Shift>> HistoryAsync(Guid employeeId, DateOnly from, DateOnly to, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.Shifts.AsNoTracking().Include(shift => shift.Activities).Where(shift => shift.EmployeeId == employeeId && shift.Date >= from && shift.Date <= to).ToArrayAsync(cancellationToken);
    }
    public async Task SaveAsync(Guid shiftId, IReadOnlyList<RuleWarning> warnings, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var state = await db.ShiftRuleGuidance.FindAsync([shiftId], cancellationToken);
        var previous = state is null ? [] : JsonSerializer.Deserialize<RuleWarning[]>(state.Warnings) ?? [];
        if (state is null)
        {
            state = new ShiftRuleGuidance { ShiftId = shiftId };
            db.ShiftRuleGuidance.Add(state);
        }
        state.Warnings = JsonSerializer.Serialize(warnings);
        await db.SaveChangesAsync(cancellationToken);
        await alerts.UpdateAsync(shiftId, warnings.Select(warning => warning.RuleId).ToArray(), warnings.Any(warning => warning.Severity == "warning"),
            warnings.Any(warning => !previous.Contains(warning)), context, cancellationToken);
    }
}
