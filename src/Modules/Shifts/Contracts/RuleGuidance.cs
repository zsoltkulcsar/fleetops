using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
namespace FleetOps.Modules.Shifts;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record RuleWarning(string RuleId, string Severity, string Message);
public interface IShiftGuidance
{
    Task<IReadOnlyList<RuleWarning>> RecomputeAsync(Shift shift, CommandContext context, CancellationToken cancellationToken);
}
public interface IShiftGuidanceStore
{
    Task<IReadOnlyList<Shift>> HistoryAsync(Guid employeeId, DateOnly from, DateOnly to, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(Guid shiftId, IReadOnlyList<RuleWarning> warnings, CommandContext context, CancellationToken cancellationToken);
}

public interface IGuidanceFailureLog
{
    void Failed(string exceptionType);
}
