using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
public interface IDefectNotificationFacts
{
    Task<IReadOnlyList<Guid>> DriversAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<Guid>> DispatchersAsync(CommandContext context, CancellationToken cancellationToken);
}
