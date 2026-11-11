using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public sealed record EmployeeLeft(Guid EmployeeId, DateOnly EndDate, string Reason, DateTimeOffset? RunningShiftEnd, string? RunningShiftReason);
public interface IEmployeeLeft
{
    Task ApplyAsync(EmployeeLeft notification, LeavePreview preview, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
