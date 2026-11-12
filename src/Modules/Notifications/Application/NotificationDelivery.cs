using System.Text.Json;
using FleetOps.Modules.Setup;
namespace FleetOps.Modules.Notifications;
public sealed class NotificationDelivery(INotificationDeliveryStore store, IEmailSender email, IPushSender push, IPushConfiguration configuration, ISettings settings, TimeProvider time) : IPostCommitNotifications
{
    public async Task TrySendAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var maximum = await settings.GetIntAsync(new("notifications.max_attempts"), null, cancellationToken);
        foreach (var id in ids.Distinct())
            await store.DeliverAsync(id, delivery => AttemptAsync(delivery, maximum, cancellationToken), cancellationToken);
    }
    private async Task<DeliveryOutcome?> AttemptAsync(PendingDelivery delivery, int maximum, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (delivery.SentAt is not null || delivery.Attempts >= maximum || delivery.NextAttemptAt > now) return null;
        try
        {
            if (!delivery.RecipientActive) throw new InvalidOperationException("recipient_unavailable");
            if (delivery.Channel != NotificationChannel.InApp && (!delivery.Office || delivery.Preferences is null)) throw new InvalidOperationException("recipient_unavailable");
            if (delivery.Channel == NotificationChannel.Email)
            {
                var enabled = delivery.TemplateCode == "daily_digest" ? delivery.Preferences!.DigestEmail : delivery.Preferences!.CriticalEmail;
                if (!enabled) throw new InvalidOperationException("delivery_disabled");
            }
            if (delivery.Channel == NotificationChannel.Push && !delivery.Preferences!.CriticalPush) throw new InvalidOperationException("delivery_disabled");
            await SendAsync(delivery, cancellationToken);
            return new(now, delivery.Attempts, null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var attempts = delivery.Attempts + 1;
            return new(null, attempts, now.AddMinutes(attempts == 1 ? 1 : attempts == 2 ? 5 : 30), exception.GetType().Name);
        }
    }
    private async Task SendAsync(PendingDelivery delivery, CancellationToken cancellationToken)
    {
        if (delivery.Channel == NotificationChannel.InApp) return;
        if (delivery.Channel == NotificationChannel.Push)
        {
            if (!configuration.Get().Enabled) throw new InvalidOperationException("push_disabled");
            if (delivery.Subscriptions.Count == 0) throw new InvalidOperationException("push_unsubscribed");
            foreach (var subscription in delivery.Subscriptions)
                await push.SendAsync(new(delivery.Id, delivery.EmployeeId, subscription.Endpoint, GenericBody(delivery.Language)), cancellationToken);
            return;
        }
        if (string.IsNullOrWhiteSpace(delivery.Email)) throw new InvalidOperationException("email_unavailable");
        var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(delivery.Parameters) ?? [];
        var body = NotificationTemplates.Render(delivery.TemplateCode, parameters, delivery.Language);
        await email.SendAsync(new(delivery.Id, delivery.EmployeeId, delivery.Email, GenericBody(delivery.Language), body), cancellationToken);
    }
    public static string GenericBody(string? language) => language == "de" ? "FleetOps: Aktion erforderlich" : "FleetOps: action needed";
}
