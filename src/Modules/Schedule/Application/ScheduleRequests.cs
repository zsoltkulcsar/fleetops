using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Schedule;
public sealed class ScheduleRequests(IScheduleRequestStore store, IScheduleEditor editor, IScheduleChanged changed, TimeProvider time) : IScheduleRequests, ICommandHandler<DecideScheduleRequestCommand, ScheduleRequestView>, IQueryHandler<ListScheduleRequestsQuery, ScheduleRequestList>
{
    public async Task RequestAsync(Guid id, Guid commandId, DateTimeOffset deviceTime, Guid cycleId, DateOnly from, DateOnly to, string? note, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (!actor.Roles.Contains(RoleCode.Driver)) throw new AuthorizationException("permission_denied", "schedule.request_change", [RoleCode.Driver]);
        if (from == default || to < from || to.DayNumber - from.DayNumber > 366 || string.IsNullOrWhiteSpace(note) || note.Length > 1000) throw new ScheduleValidationException(["from", "to", "note"]);
        await store.LockAsync(context, cancellationToken);
        var cycle = await store.CycleAsync(cycleId, context, cancellationToken);
        if (cycle is null || cycle.EmployeeId != actor.EmployeeId || cycle.Status is CycleStatus.Done or CycleStatus.Cancelled) throw new ScheduleConflictException("cycle_not_requestable");
        if (await store.LoadAsync(id, context, cancellationToken) is not null) throw new ScheduleConflictException("request_already_exists");
        var request = new ScheduleChangeRequest { Id = id, EmployeeId = actor.EmployeeId, CycleId = cycleId, ProposedFrom = from, ProposedTo = to, Note = note.Trim(), CommandId = commandId, DeviceTime = deviceTime };
        await store.SaveAsync(request, actor, "schedule_change_requested", true, false, context, cancellationToken);
    }
    public async Task<ScheduleRequestView> HandleAsync(DecideScheduleRequestCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (!command.Accept && (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 2000)) throw new ScheduleValidationException(["reason"]);
        await store.LockAsync(context, cancellationToken);
        var request = await store.LoadAsync(command.Id, context, cancellationToken) ?? throw new ScheduleConflictException("not_found");
        if (request.Status != ScheduleRequestStatus.Open) throw new ScheduleConflictException("state_conflict");
        if (command.Accept)
        {
            var cycle = await store.CycleAsync(request.CycleId, context, cancellationToken) ?? throw new ScheduleConflictException("cycle_not_found");
            if (cycle.EmployeeId != request.EmployeeId) throw new ScheduleConflictException("cycle_changed");
            ManualCycleRules.Validate(cycle.EmployeeId, cycle.VehicleId, request.ProposedFrom, request.ProposedTo, request.Note);
            var mutation = await editor.EditAsync(new(cycle.Id, cycle.Version, cycle.EmployeeId, cycle.VehicleId, request.ProposedFrom, request.ProposedTo, request.Note), actor, context, cancellationToken);
            await changed.NotifyAsync(mutation, context, cancellationToken);
        }
        request.Status = command.Accept ? ScheduleRequestStatus.Accepted : ScheduleRequestStatus.Declined;
        request.DeclineReason = command.Accept ? null : command.Reason!.Trim();
        request.DecidedBy = actor.EmployeeId;
        request.DecidedAt = time.GetUtcNow();
        await store.SaveAsync(request, actor, command.Accept ? "schedule_request_accepted" : request.DeclineReason!, false, true, context, cancellationToken);
        return View(request);
    }
    public async Task<ScheduleRequestList> HandleAsync(ListScheduleRequestsQuery query, AccessContext actor, CancellationToken cancellationToken) => new(await store.ListAsync(null, cancellationToken));
    public static ScheduleRequestView View(ScheduleChangeRequest request) => new(request.Id, request.EmployeeId, request.CycleId, request.ProposedFrom, request.ProposedTo, request.Note, request.Status, request.DeclineReason, request.DecidedBy, request.DecidedAt);
}
