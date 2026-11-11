using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public sealed class LeavePersonHandler(IPeopleStore people, ILeaveStore leave, IEmployeeLeft effects, ICompanyTimeService clock, TimeProvider time) : ICommandHandler<LeavePersonCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(LeavePersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500) throw new PersonValidationException(["reason"]);
        var employee = await people.LockAsync(command.SubjectId ?? Guid.Empty, context, cancellationToken) ?? throw new PersonConflictException("not_found");
        if (employee.Status is not (EmployeeStatus.Active or EmployeeStatus.Suspended)) throw new PersonConflictException("state_conflict");
        var today = clock.ToCompanyDate(time.GetUtcNow());
        var endDate = command.EndDate ?? today;
        if (endDate < employee.StartDate) throw new PersonValidationException(["endDate"]);
        var preview = await leave.PreviewAsync(employee.Id, endDate, context, cancellationToken);
        if (preview.RunningShiftId.HasValue && (command.RunningShiftEnd is null || string.IsNullOrWhiteSpace(command.RunningShiftReason)))
            throw new PersonConflictException("running_shift_needs_end");
        if ((command.RunningShiftEnd is DateTimeOffset shiftEnd && clock.ToCompanyDate(shiftEnd) > endDate) || command.RunningShiftEnd > time.GetUtcNow() || command.RunningShiftReason?.Length > 500) throw new PersonValidationException(["runningShiftEnd", "runningShiftReason"]);
        var previousStatus = employee.Status.ToString().ToLowerInvariant();
        var previousEndDate = employee.EndDate;
        employee.EndDate = endDate;
        employee.LeaveReason = command.Reason.Trim();
        employee.LeaveActorId = actor.EmployeeId;
        employee.LeaveShiftEnd = command.RunningShiftEnd;
        employee.LeaveShiftReason = command.RunningShiftReason?.Trim();
        if (endDate <= today)
        {
            await effects.ApplyAsync(new(employee.Id, endDate, command.Reason.Trim(), command.RunningShiftEnd, command.RunningShiftReason), preview, actor, context, cancellationToken);
            employee.Status = EmployeeStatus.Left;
        }
        await people.SaveAsync(employee, false, [new("status", previousStatus, employee.Status.ToString().ToLowerInvariant(), command.Reason.Trim()), new("endDate", previousEndDate?.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), command.Reason.Trim())], actor, context, cancellationToken);
        return new(employee.Id, employee.Status.ToString().ToLowerInvariant());
    }
}
