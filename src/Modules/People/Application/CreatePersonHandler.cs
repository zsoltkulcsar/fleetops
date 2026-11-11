using System.Text.Json;
using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class CreatePersonHandler(IPeopleStore store, IPersonEnrolment enrolment) : ICommandHandler<CreatePersonCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(CreatePersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        PersonRules.Validate(command.Fields, command.Roles);
        var employee = new Employee
        {
            Id = Guid.NewGuid(),
            Status = EmployeeStatus.Onboarding,
            Roles = JsonSerializer.Serialize(command.Roles),
            DepotId = await store.DepotAsync(context, cancellationToken)
        };
        await PersonEditing.ApplyAsync(store, employee, command.Fields, actor, context, true, cancellationToken);
        var sent = command.Roles!.Contains("driver") && await enrolment.SendAsync(employee.Id, actor, context, cancellationToken);
        return new(employee.Id, PersonRules.Status(employee), sent);
    }
}
