using System.Collections.Concurrent;
namespace FleetOps.Modules.Notifications;
public sealed class DemoEmailSender : IEmailSender
{
    private readonly ConcurrentDictionary<Guid, EmailMessage> inbox = new();
    public IReadOnlyCollection<EmailMessage> Inbox => inbox.Values.ToArray();
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        inbox.TryAdd(message.NotificationId, message);
        return Task.CompletedTask;
    }
}
