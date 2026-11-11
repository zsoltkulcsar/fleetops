using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class ActivatePersonHandler(IPeopleStore store, ICompanyTimeService clock, TimeProvider time) : ICommandHandler<ActivatePersonCommand, PersonSaveResult>
{
    public async Task<PersonSaveResult> HandleAsync(ActivatePersonCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var employee = await store.LockAsync(command.SubjectId ?? Guid.Empty, context, cancellationToken) ?? throw new PersonConflictException("not_found");
        if (employee.Status != EmployeeStatus.Onboarding) throw new PersonConflictException("state_conflict");
        var today = clock.ToCompanyDate(time.GetUtcNow());
        var facts = (await store.ReadAsync(today, employee.Id, context, cancellationToken)).Single();
        var missing = PersonRules.Checklist(facts, today, false).Where(item => !PersonRules.Valid(item)).Select(item => item.Type).ToArray();
        if (missing.Any(type => !PersonRules.Blocking(type))) throw new PersonConflictException("activation_incomplete", missing);
        if (missing.Length > 0 && !command.AcceptWarning) throw new PersonConflictException("activation_needs_warning", missing);
        employee.Status = EmployeeStatus.Active;
        var audit = new List<PersonAudit> { new("status", "onboarding", "active") };
        if (missing.Length > 0) audit.Add(new("activationWarning", null, string.Join(",", missing)));
        await store.SaveAsync(employee, false, audit, actor, context, cancellationToken);
        return new(employee.Id, "active");
    }
}
