using FleetOps.Kernel.Authorization;
using FleetOps.People;

namespace FleetOps.Modules.Schedule;

public sealed class CyclePatternChangedHandler(ICycleStore store)
{
    public async Task<int> HandleAsync(CyclePatternChanged notification, CyclePattern pattern, DateOnly today, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await store.RemoveFutureGeneratedAsync(notification.EmployeeId, today, context, actorId, cancellationToken);
        var existing = await store.LoadCyclesAsync(notification.EmployeeId, context, cancellationToken);
        var missing = CycleGeneration.Missing(notification.EmployeeId, pattern, today, existing);
        await store.AddCyclesAsync(missing, context, actorId, cancellationToken);
        return missing.Count;
    }
}
