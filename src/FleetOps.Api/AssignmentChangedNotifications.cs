using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Maintenance;
using FleetOps.Modules.Notifications;
namespace FleetOps.Api;

public sealed class AssignmentChangedNotifications(INotifications notifications) : IAssignmentChanged
{
    public async Task NotifyAsync(Guid assignmentId, IReadOnlyCollection<Guid> recipients, CommandContext context, CancellationToken cancellationToken)
    {
        foreach (var employeeId in recipients.Distinct())
            await notifications.AddAsync(employeeId, "assignment_changed", new { assignmentId }, null, context, cancellationToken);
    }
}
