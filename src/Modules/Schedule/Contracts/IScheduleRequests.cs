using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Schedule;
public interface IScheduleRequests
{
    Task RequestAsync(Guid id, Guid commandId, DateTimeOffset deviceTime, Guid cycleId, DateOnly from, DateOnly to, string? note, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
