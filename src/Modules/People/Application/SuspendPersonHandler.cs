using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public sealed class SuspendPersonHandler(IPeopleStore store) : ICommandHandler<SuspendPersonCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(SuspendPersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500) throw new PersonValidationException(["reason"]);
        var employee = await store.LockAsync(command.SubjectId ?? Guid.Empty, context, cancellationToken) ?? throw new PersonConflictException("not_found");
        if (employee.Status != EmployeeStatus.Active) throw new PersonConflictException("state_conflict");
        employee.Status = EmployeeStatus.Suspended;
        
        await store.SaveAsync(employee, false, [new("status", "active", "suspended", command.Reason.Trim())], actor, context, cancellationToken);
        return new(employee.Id, "suspended");
    }
}
