using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
namespace FleetOps.Modules.Shifts;
public sealed class ShiftGuidance(IComplianceRuleStore rules, IShiftGuidanceStore store, TimeProvider clock, IGuidanceFailureLog logger) : IShiftGuidance
{
    public async Task<IReadOnlyList<RuleWarning>> RecomputeAsync(Shift shift, CommandContext context, CancellationToken cancellationToken)
    {
        await context.DbTransaction.SaveAsync("shift_guidance", cancellationToken);
        try
        {
            var values = await rules.ReadAsync(context, cancellationToken);
            var history = await store.HistoryAsync(shift.EmployeeId, shift.Date.AddDays(-14), shift.Date, context, cancellationToken);
            var warnings = ComplianceRuleEngine.ShiftWarnings(shift, history, values, clock.GetUtcNow());
            await store.SaveAsync(shift.Id, warnings, context, cancellationToken);
            await context.DbTransaction.ReleaseAsync("shift_guidance", cancellationToken);
            return warnings;
        }
        catch (Exception exception)
        {
            await context.DbTransaction.RollbackAsync("shift_guidance", CancellationToken.None);
            logger.Failed(exception.GetType().Name);
            return [];
        }
    }
}
