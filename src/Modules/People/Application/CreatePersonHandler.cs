using System.Text.Json;
using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class CreatePersonHandler(IPeopleStore store, IPersonEnrolment enrolment, IStepUpContextAccessor? stepUp = null) : ICommandHandler<CreatePersonCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(CreatePersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        PersonRules.Validate(command.Fields, command.Roles);
        if (command.Roles!.Contains("owner"))
        {
            if (!actor.Roles.Contains(RoleCode.Owner)) throw new AuthorizationException("permission_denied", "people.roles", [RoleCode.Owner]);
            if (stepUp?.HasFreshStepUp != true) throw new AuthorizationException("step_up_required", "people.roles", [RoleCode.Owner]);
        }
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
