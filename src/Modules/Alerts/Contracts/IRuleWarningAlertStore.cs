using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public interface IRuleWarningAlertStore
{
    Task<Alert?> FindAsync(Guid shiftId, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(Alert alert, string? note, Guid? actorId, CommandContext context, CancellationToken cancellationToken);
}
