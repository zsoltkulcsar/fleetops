using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Schedule;
public interface IScheduleRequestStore
{
    Task LockAsync(CommandContext context, CancellationToken cancellationToken);
    Task<ScheduleChangeRequest?> LoadAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task<Cycle?> CycleAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(ScheduleChangeRequest request, AccessContext actor, string reason, bool replace, bool notify, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScheduleRequestView>> ListAsync(Guid? employeeId, CancellationToken cancellationToken);
}
