using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Schedule;

public interface ICycleStateWriter
{
    Task SaveAsync(Cycle cycle, Guid actorId, CommandContext context, CancellationToken cancellationToken);
}

public sealed class CycleCompletion(ICycleStore store, ICycleStateWriter writer)
{
    public async Task FinishAsync(Guid employeeId, DateOnly shiftDate, DateTimeOffset finishedAt, CommandContext context, CancellationToken cancellationToken)
    {
        var cycles = await store.LoadCyclesAsync(employeeId, context, cancellationToken);
        var cycle = cycles.Where(cycle => cycle.From <= shiftDate && (cycle.To == shiftDate || cycle.Status == CycleStatus.Overrun) &&
            cycle.Status is CycleStatus.Planned or CycleStatus.Active or CycleStatus.Overrun).OrderByDescending(cycle => cycle.From).FirstOrDefault();
        if (cycle is null) return;
        cycle.Status = CycleStatus.Done;
        cycle.FinishedAt = finishedAt;
        cycle.Version++;
        await writer.SaveAsync(cycle, employeeId, context, cancellationToken);
    }

    public static bool MarkOverrun(Cycle cycle, DateOnly today)
    {
        if (cycle.To >= today || cycle.Status is not (CycleStatus.Planned or CycleStatus.Active)) return false;
        cycle.Status = CycleStatus.Overrun;
        cycle.Version++;
        return true;
    }
}
