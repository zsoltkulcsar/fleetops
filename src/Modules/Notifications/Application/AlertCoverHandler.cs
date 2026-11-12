using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
public sealed class AlertCoverHandler(IAlertCoverStore store) :
    IQueryHandler<GetAlertCoversQuery, AlertCoverList>, ICommandHandler<SaveAlertCoverCommand, AlertCoverList>, ICommandHandler<DeleteAlertCoverCommand, bool>
{
    public Task<AlertCoverList> HandleAsync(GetAlertCoversQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        OfficeNotificationAccess.Demand(actor);
        return store.ListAsync(actor.EmployeeId, null, cancellationToken);
    }
    public async Task<AlertCoverList> HandleAsync(SaveAlertCoverCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        OfficeNotificationAccess.Demand(actor);
        if (command.ToId == Guid.Empty || command.ToId == actor.EmployeeId) throw new NotificationException("validation_error", "toId");
        if (command.From == default || command.To < command.From) throw new NotificationException("validation_error", "from", "to");
        if (!await store.SaveAsync(actor.EmployeeId, command, context, cancellationToken)) throw new NotificationException("validation_error", "toId", "id", "from", "to");
        return await store.ListAsync(actor.EmployeeId, context, cancellationToken);
    }
    public Task<bool> HandleAsync(DeleteAlertCoverCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        OfficeNotificationAccess.Demand(actor);
        return store.DeleteAsync(actor.EmployeeId, command.Id, context, cancellationToken);
    }
}
