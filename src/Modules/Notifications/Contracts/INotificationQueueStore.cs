using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
public sealed record NotificationRecipient(Guid EmployeeId, NotifyPreferences Preferences, bool Office, bool Subscribed);
public interface INotificationQueueStore
{
    Task<IReadOnlyList<NotificationRecipient>> RecipientsAsync(Guid employeeId, bool includeCover, CommandContext context, CancellationToken cancellationToken);
    Task<Guid> InsertAsync(Guid employeeId, string template, object parameters, Guid? alertId, NotificationChannel channel, CommandContext context, CancellationToken cancellationToken);
}
