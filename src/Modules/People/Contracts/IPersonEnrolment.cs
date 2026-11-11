using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public interface IPersonEnrolment
{
    Task<bool> SendAsync(Guid id, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
