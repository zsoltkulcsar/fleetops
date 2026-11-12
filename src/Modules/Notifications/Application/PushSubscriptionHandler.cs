using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
public sealed class PushSubscriptionHandler(IPushSubscriptionStore store, IPushConfiguration configuration) :
    IQueryHandler<GetPushStatusQuery, PushStatus>, ICommandHandler<SavePushSubscriptionCommand, bool>, ICommandHandler<DeletePushSubscriptionCommand, bool>
{
    public Task<PushStatus> HandleAsync(GetPushStatusQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        OfficeNotificationAccess.Demand(actor);
        var status = configuration.Get();
        return Task.FromResult(new PushStatus(status.Enabled, status.PublicKey));
    }
    public async Task<bool> HandleAsync(SavePushSubscriptionCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        OfficeNotificationAccess.Demand(actor);
        if (!configuration.Get().Enabled) throw new NotificationException("push_disabled");
        ValidateEndpoint(command.Endpoint);
        if (!ValidKey(command.P256dh, 65)) throw new NotificationException("validation_error", "p256dh");
        if (!ValidKey(command.Auth, 16)) throw new NotificationException("validation_error", "auth");
        await store.SaveAsync(actor.EmployeeId, command.Endpoint, command.P256dh, command.Auth, context, cancellationToken);
        return true;
    }
    public Task<bool> HandleAsync(DeletePushSubscriptionCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        OfficeNotificationAccess.Demand(actor);
        ValidateEndpoint(command.Endpoint);
        return store.DeleteAsync(actor.EmployeeId, command.Endpoint, context, cancellationToken);
    }
    private static void ValidateEndpoint(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 2048 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new NotificationException("validation_error", "endpoint");
    }
    private static bool ValidKey(string value, int length)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) return false;
        try
        {
            var normalized = value.Replace('-', '+').Replace('_', '/');
            return Convert.FromBase64String(normalized.PadRight((normalized.Length + 3) / 4 * 4, '=')).Length == length;
        }
        catch (FormatException) { return false; }
    }
}
