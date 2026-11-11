using System.Text.Json;
using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class EditPersonHandler(IPeopleStore store) : ICommandHandler<EditPersonCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(EditPersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var employee = await store.LockAsync(command.SubjectId ?? Guid.Empty, context, cancellationToken) ?? throw new PersonConflictException("not_found");
        PersonRules.Validate(command.Fields, JsonSerializer.Deserialize<string[]>(employee.Roles));
        await PersonEditing.ApplyAsync(store, employee, command.Fields, actor, context, false, cancellationToken);
        return new(employee.Id, PersonRules.Status(employee));
    }
}
