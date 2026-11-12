namespace FleetOps.Modules.Notifications;
public sealed record EmailMessage(Guid NotificationId, Guid EmployeeId, string Address, string Subject, string Body);
public sealed record PushMessage(Guid NotificationId, Guid EmployeeId, string Endpoint, string Body);
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
public interface IPushSender
{
    Task SendAsync(PushMessage message, CancellationToken cancellationToken);
}
public sealed record PushConfiguration(bool Enabled, string? PublicKey);
public interface IPushConfiguration
{
    PushConfiguration Get();
}
public sealed record PendingDelivery(Guid Id, Guid EmployeeId, string TemplateCode, string Parameters, NotificationChannel Channel, string Language, string? Email, IReadOnlyList<PushSubscription> Subscriptions, bool RecipientActive, bool Office, NotifyPreferences? Preferences, int Attempts, DateTimeOffset? SentAt, DateTimeOffset? NextAttemptAt);
public sealed record DeliveryOutcome(DateTimeOffset? SentAt, int Attempts, DateTimeOffset? NextAttemptAt, string? ErrorType = null);
public interface INotificationDeliveryStore
{
    Task DeliverAsync(Guid id, Func<PendingDelivery, Task<DeliveryOutcome?>> send, CancellationToken cancellationToken);
}
