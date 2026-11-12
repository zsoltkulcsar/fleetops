using System.Collections.Concurrent;
namespace FleetOps.Modules.Notifications;
public sealed class DemoPushSender : IPushSender
{
    private readonly ConcurrentDictionary<(Guid, string), PushMessage> recorded = new();
    public IReadOnlyCollection<PushMessage> Recorded => recorded.Values.ToArray();
    public Task SendAsync(PushMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        recorded.TryAdd((message.NotificationId, message.Endpoint), message);
        return Task.CompletedTask;
    }
}
