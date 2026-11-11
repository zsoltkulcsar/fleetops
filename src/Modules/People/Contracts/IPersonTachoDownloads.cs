using FleetOps.Kernel.Authorization;
namespace FleetOps.People;

public interface IPersonTachoDownloads
{
    Task<Employee?> LockAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(Employee person, Guid actorId, CommandContext context, CancellationToken cancellationToken);
}
