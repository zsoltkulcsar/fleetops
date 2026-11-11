using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public sealed class EmployeeLeftHandler(IEmployeeLeftStore store) : IEmployeeLeft
{
    public async Task ApplyAsync(EmployeeLeft notification, LeavePreview preview, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (preview.RunningShiftId.HasValue)
        {
            if (notification.RunningShiftEnd is null || string.IsNullOrWhiteSpace(notification.RunningShiftReason)) throw new PersonConflictException("running_shift_needs_end");
            await store.EndShiftAsync(notification, preview, actor, context, cancellationToken);
        }
        await store.ApplyAsync(notification, preview, context, cancellationToken);
    }
}
