using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public interface IPlanningAlertFacts
{
    Task<bool> OpenScheduleRequestAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task<bool> ShiftDuringAbsenceAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
}
