using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
public sealed class AlertCover
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid FromId { get; set; }
    public Guid ToId { get; set; }
    public DateOnly From { get; set; }
    public DateOnly To { get; set; }
}
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AlertCoverItem(Guid Id, Guid ToId, string Colleague, DateOnly From, DateOnly To);
[VisibleTo(RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record AlertCoverList(IReadOnlyList<AlertCoverItem> Items) : IReadModel;
[QueryPolicy("notifications.mine")]
public sealed record GetAlertCoversQuery(Guid? SubjectId) : IQuery<AlertCoverList>, IHasSubject;
[CommandPolicy("notifications.mine")]
public sealed record SaveAlertCoverCommand(Guid? Id, Guid ToId, DateOnly From, DateOnly To, Guid? SubjectId) : ICommand<AlertCoverList>, IHasSubject;
[CommandPolicy("notifications.mine")]
public sealed record DeleteAlertCoverCommand(Guid Id, Guid? SubjectId) : ICommand<bool>, IHasSubject;
public interface IAlertCoverStore
{
    Task<AlertCoverList> ListAsync(Guid employeeId, CommandContext? context, CancellationToken cancellationToken);
    Task<bool> SaveAsync(Guid employeeId, SaveAlertCoverCommand command, CommandContext context, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(Guid employeeId, Guid id, CommandContext context, CancellationToken cancellationToken);
}
