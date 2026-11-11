using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public sealed class ApplyScheduledLeaveHandler(IPeopleStore people, ICommandHandler<LeavePersonCommand, PersonSaveResult> leave) : ICommandHandler<ApplyScheduledLeaveCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(ApplyScheduledLeaveCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var employee = await people.LockAsync(command.SubjectId ?? Guid.Empty, context, cancellationToken) ?? throw new PersonConflictException("not_found");
        if (employee.Status == EmployeeStatus.Left || employee.EndDate is not DateOnly endDate || endDate > command.Today || employee.LeaveReason is null || employee.LeaveActorId is not Guid actorId)
            return new(employee.Id, employee.Status.ToString().ToLowerInvariant());
        return await leave.HandleAsync(new(employee.Id, endDate, employee.LeaveReason, employee.LeaveShiftEnd, employee.LeaveShiftReason), AccessContext.For(actorId, RoleCode.Hr), context, cancellationToken);
    }
}
