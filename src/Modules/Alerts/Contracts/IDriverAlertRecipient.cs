using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public interface IDriverAlertRecipient
{
    Task<Guid?> ShiftOwnerAsync(Guid shiftId, CommandContext context, CancellationToken cancellationToken);
}
