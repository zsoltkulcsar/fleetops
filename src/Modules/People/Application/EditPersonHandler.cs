using System.Text.Json;
using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class EditPersonHandler(IPeopleStore store, FleetOps.Kernel.ICompanyTimeService? companyTime = null, TimeProvider? time = null) : ICommandHandler<EditPersonCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(EditPersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var employee = await store.LockAsync(command.SubjectId ?? Guid.Empty, context, cancellationToken) ?? throw new PersonConflictException("not_found");
        PersonRules.Validate(command.Fields, JsonSerializer.Deserialize<string[]>(employee.Roles));
        if (JsonSerializer.Deserialize<string[]>(employee.Roles)?.Contains("driver") == true && employee.Phone != command.Fields.Phone)
            throw new PersonValidationException(["phone"]);
        await PersonEditing.ApplyAsync(store, employee, command.Fields, actor, context, false, cancellationToken, (companyTime ?? new FleetOps.Kernel.CompanyTimeService(FleetOps.Kernel.CompanyTimeService.Resolve("Europe/Berlin"))).ToCompanyDate((time ?? TimeProvider.System).GetUtcNow()).Year);
        return new(employee.Id, PersonRules.Status(employee));
    }
}
