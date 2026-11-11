using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public sealed class ReactivatePersonHandler(IPeopleStore store) : ICommandHandler<ReactivatePersonCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(ReactivatePersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 500) throw new PersonValidationException(["reason"]);
        var employee = await store.LockAsync(command.SubjectId ?? Guid.Empty, context, cancellationToken) ?? throw new PersonConflictException("not_found");
        if (employee.Status != EmployeeStatus.Suspended) throw new PersonConflictException("state_conflict");
        employee.Status = EmployeeStatus.Active;
        
        await store.SaveAsync(employee, false, [new("status", "suspended", "active", command.Reason.Trim())], actor, context, cancellationToken);
        return new(employee.Id, "active");
    }
}
