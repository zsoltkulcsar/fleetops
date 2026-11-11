using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
public sealed class AbsenceCommands(IAbsenceStore store) : ICommandHandler<RecordAbsenceCommand, AbsenceView>, ICommandHandler<DecideAbsenceCommand, AbsenceView>, ICommandHandler<CancelAbsenceCommand, AbsenceView>, ICommandHandler<EditAbsenceCommand, AbsenceView>
{
    public async Task<AbsenceView> HandleAsync(RecordAbsenceCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        AbsenceRules.Validate(command.From, command.To, command.HalfDay, command.Type, command.Note);
        var hr = actor.Roles.Contains(RoleCode.Hr);
        if (!hr && command.Type is not (AbsenceType.Sick or AbsenceType.Other)) throw new AuthorizationException("permission_denied", "absences.record", [RoleCode.Hr]);
        await store.LockAsync(command.EmployeeId, context, cancellationToken);
        if (await store.FactsAsync(command.EmployeeId, context, cancellationToken) is null) throw new AbsenceValidationException("employeeId");
        var absence = new Absence { EmployeeId = command.EmployeeId, From = command.From, To = command.To, HalfDay = command.HalfDay, Type = command.Type, Note = command.Note, Status = AbsenceStatus.Approved, DecidedBy = actor.EmployeeId };
        await AbsenceRules.CheckShiftsAsync(store, absence, command.ConfirmOverlap, context, cancellationToken);
        await store.SaveAsync(absence, actor, "absence_recorded", false, !hr, context, cancellationToken);
        return AbsenceRules.View(absence);
    }
    public async Task<AbsenceView> HandleAsync(DecideAbsenceCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var reason = command.Approve ? "absence_approved" : AbsenceRules.Reason(command.Reason);
        var absence = await Locked(command.Id, context, cancellationToken);
        if (absence.Status != AbsenceStatus.Requested) throw new AbsenceConflictException("state_conflict");
        if (command.Approve) await AbsenceRules.CheckShiftsAsync(store, absence, command.ConfirmOverlap, context, cancellationToken);
        absence.Status = command.Approve ? AbsenceStatus.Approved : AbsenceStatus.Declined;
        absence.DeclineReason = command.Approve ? null : reason;
        absence.DecidedBy = actor.EmployeeId;
        await store.SaveAsync(absence, actor, reason, true, false, context, cancellationToken);
        return AbsenceRules.View(absence);
    }
    public async Task<AbsenceView> HandleAsync(CancelAbsenceCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var reason = AbsenceRules.Reason(command.Reason);
        var absence = await Locked(command.Id, context, cancellationToken);
        if (absence.Status != AbsenceStatus.Approved) throw new AbsenceConflictException("state_conflict");
        absence.Status = AbsenceStatus.Cancelled;
        absence.CancelReason = reason;
        absence.DecidedBy = actor.EmployeeId;
        await store.SaveAsync(absence, actor, reason, true, false, context, cancellationToken);
        return AbsenceRules.View(absence);
    }
    public async Task<AbsenceView> HandleAsync(EditAbsenceCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var reason = AbsenceRules.Reason(command.Reason);
        var absence = await Locked(command.Id, context, cancellationToken);
        if (absence.Status is AbsenceStatus.Cancelled or AbsenceStatus.Declined) throw new AbsenceConflictException("state_conflict");
        AbsenceRules.Validate(command.From, command.To, command.HalfDay, command.Type, command.Type == AbsenceType.Sick ? null : absence.Note);
        absence.From = command.From;
        absence.To = command.To;
        absence.HalfDay = command.HalfDay;
        absence.Type = command.Type;
        if (absence.Type == AbsenceType.Sick) absence.Note = null;
        if (absence.Status == AbsenceStatus.Approved) await AbsenceRules.CheckShiftsAsync(store, absence, command.ConfirmOverlap, context, cancellationToken);
        await store.SaveAsync(absence, actor, reason, false, false, context, cancellationToken);
        return AbsenceRules.View(absence);
    }
    private async Task<Absence> Locked(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        var absence = await store.LoadAsync(id, context, cancellationToken) ?? throw new AbsenceConflictException("not_found");
        await store.LockAsync(absence.EmployeeId, context, cancellationToken);
        return await store.LoadAsync(id, context, cancellationToken) ?? throw new AbsenceConflictException("not_found");
    }
}
