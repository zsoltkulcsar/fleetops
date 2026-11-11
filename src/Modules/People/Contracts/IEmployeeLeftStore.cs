using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public interface IEmployeeLeftStore
{
    Task EndShiftAsync(EmployeeLeft notification, LeavePreview preview, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task ApplyAsync(EmployeeLeft notification, LeavePreview preview, CommandContext context, CancellationToken cancellationToken);
}
