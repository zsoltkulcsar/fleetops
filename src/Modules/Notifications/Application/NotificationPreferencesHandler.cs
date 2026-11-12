using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
public sealed class NotificationPreferencesHandler(INotificationPreferencesStore store) :
    IQueryHandler<GetNotifyPreferencesQuery, NotifyPreferences>, ICommandHandler<SaveNotifyPreferencesCommand, NotifyPreferences>
{
    public async Task<NotifyPreferences> HandleAsync(GetNotifyPreferencesQuery query, AccessContext actor, CancellationToken cancellationToken) =>
        await store.GetAsync(actor.EmployeeId, cancellationToken) ?? throw new NotificationException("not_found");
    public async Task<NotifyPreferences> HandleAsync(SaveNotifyPreferencesCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.Preferences is null) throw new NotificationException("validation_error", "preferences");
        if (!await store.SaveAsync(actor.EmployeeId, command.Preferences, context, cancellationToken)) throw new NotificationException("not_found");
        return command.Preferences;
    }
}
