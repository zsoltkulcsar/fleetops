using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
public sealed class PushSubscription
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid EmployeeId { get; set; }
    public string Endpoint { get; set; } = "";
    public string P256dh { get; set; } = "";
    public string Auth { get; set; } = "";
}
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record PushStatus(bool Enabled, string? PublicKey) : IReadModel;
[QueryPolicy("notifications.mine")]
public sealed record GetPushStatusQuery(Guid? SubjectId) : IQuery<PushStatus>, IHasSubject;
[CommandPolicy("notifications.mine")]
public sealed record SavePushSubscriptionCommand(string Endpoint, string P256dh, string Auth, Guid? SubjectId) : ICommand<bool>, IHasSubject;
[CommandPolicy("notifications.mine")]
public sealed record DeletePushSubscriptionCommand(string Endpoint, Guid? SubjectId) : ICommand<bool>, IHasSubject;
public interface IPushSubscriptionStore
{
    Task SaveAsync(Guid employeeId, string endpoint, string p256dh, string auth, CommandContext context, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid employeeId, string endpoint, CommandContext context, CancellationToken cancellationToken);
}
