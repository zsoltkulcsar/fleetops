using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Maintenance;

public sealed class AssignmentChanges(IRepairStore store, IAssignmentChanged notifications, IChangedSubjects changed)
{
    public async Task<IReadOnlyList<Guid>> RecipientsAsync(Assignment assignment, CommandContext context, CancellationToken cancellationToken)
        => (await store.CycleDriversAsync(assignment.VehicleId, assignment.From, assignment.To, context, cancellationToken))
            .Append(assignment.EmployeeId).Distinct().ToArray();

    public async Task SaveAsync(Assignment assignment, AccessContext actor, IReadOnlyList<Guid> previousRecipients,
        CommandContext context, CancellationToken cancellationToken)
    {
        await store.SaveAssignmentAsync(assignment, actor.EmployeeId, context, cancellationToken);
        changed.Add("employee", assignment.EmployeeId);
        changed.Add("vehicle", assignment.VehicleId);
        var recipients = previousRecipients.Concat(await RecipientsAsync(assignment, context, cancellationToken)).Distinct().ToArray();
        await notifications.NotifyAsync(assignment.Id, recipients, context, cancellationToken);
    }
}
