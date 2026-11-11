using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Maintenance;
public interface IAssignmentChanged
{
    Task NotifyAsync(Guid assignmentId, IReadOnlyCollection<Guid> recipients, CommandContext context, CancellationToken cancellationToken);
}
