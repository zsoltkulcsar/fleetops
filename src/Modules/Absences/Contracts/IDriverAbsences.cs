using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
public interface IDriverAbsences
{
    Task RequestAsync(Guid id, Guid commandId, DateTimeOffset deviceTime, DateOnly from, DateOnly to, HalfDay? halfDay, string? note, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task CancelAsync(Guid id, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
