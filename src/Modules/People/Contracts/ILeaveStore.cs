using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public interface ILeaveStore
{
    Task<LeavePreview> PreviewAsync(Guid employeeId, DateOnly endDate, CommandContext? context, CancellationToken cancellationToken);
}
