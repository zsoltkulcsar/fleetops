using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public sealed class RehirePersonHandler(IPeopleStore store) : ICommandHandler<RehirePersonCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(RehirePersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500) throw new PersonValidationException(["reason"]);
        var employee = await store.LockAsync(command.SubjectId ?? Guid.Empty, context, cancellationToken) ?? throw new PersonConflictException("not_found");
        if (employee.Status != EmployeeStatus.Left) throw new PersonConflictException("state_conflict");
        employee.Status = EmployeeStatus.Active;
        var previousEndDate = employee.EndDate;
        employee.EndDate = null;
        employee.LeaveReason = null;
        employee.LeaveActorId = null;
        employee.LeaveShiftEnd = null;
        employee.LeaveShiftReason = null;
        await store.SaveAsync(employee, false, [new("status", "left", "active", command.Reason.Trim()), new("endDate", previousEndDate?.ToString("yyyy-MM-dd"), null, command.Reason.Trim())], actor, context, cancellationToken);
        return new(employee.Id, "active");
    }
}
