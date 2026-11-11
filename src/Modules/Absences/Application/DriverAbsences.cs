using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
public sealed class DriverAbsences(IAbsenceStore store) : IDriverAbsences
{
    public async Task RequestAsync(Guid id, Guid commandId, DateTimeOffset deviceTime, DateOnly from, DateOnly to, HalfDay? halfDay, string? note, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (!actor.Roles.Contains(RoleCode.Driver)) throw new AuthorizationException("permission_denied", "absences.vacation.request", [RoleCode.Driver]);
        AbsenceRules.Validate(from, to, halfDay, AbsenceType.Vacation, note);
        await store.LockAsync(actor.EmployeeId, context, cancellationToken);
        if (await store.LoadAsync(id, context, cancellationToken) is not null) throw new AbsenceConflictException("absence_already_exists");
        var absence = new Absence { Id = id, EmployeeId = actor.EmployeeId, CommandId = commandId, DeviceTime = deviceTime, From = from, To = to, HalfDay = halfDay, Type = AbsenceType.Vacation, Status = AbsenceStatus.Requested, Note = note };
        await store.SaveAsync(absence, actor, "absence_requested", false, false, context, cancellationToken);
    }
    public async Task CancelAsync(Guid id, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await store.LockAsync(actor.EmployeeId, context, cancellationToken);
        var absence = await store.LoadAsync(id, context, cancellationToken);
        if (absence is null || absence.EmployeeId != actor.EmployeeId || absence.Status != AbsenceStatus.Requested) throw new AbsenceConflictException("not_cancellable");
        absence.Status = AbsenceStatus.Cancelled;
        await store.SaveAsync(absence, actor, "driver_cancelled_request", false, false, context, cancellationToken);
    }
}
