using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record NotifyPreferences(bool CriticalPush = true, bool CriticalEmail = true, bool DigestEmail = true) : IReadModel;
[QueryPolicy("notifications.mine")]
public sealed record GetNotifyPreferencesQuery(Guid? SubjectId) : IQuery<NotifyPreferences>, IHasSubject;
[CommandPolicy("notifications.mine")]
public sealed record SaveNotifyPreferencesCommand(NotifyPreferences Preferences, Guid? SubjectId) : ICommand<NotifyPreferences>, IHasSubject;
public interface INotificationPreferencesStore
{
    Task<NotifyPreferences?> GetAsync(Guid employeeId, CancellationToken cancellationToken);
    Task<bool> SaveAsync(Guid employeeId, NotifyPreferences preferences, CommandContext context, CancellationToken cancellationToken);
}
