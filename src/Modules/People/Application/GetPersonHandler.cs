using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class GetPersonHandler(IPeopleStore store, ICompanyTimeService clock, TimeProvider time) : IQueryHandler<GetPersonQuery, PersonDetail>
{
    public async Task<PersonDetail> HandleAsync(GetPersonQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var today = clock.ToCompanyDate(time.GetUtcNow());
        var facts = (await store.ReadAsync(today, query.SubjectId ?? Guid.Empty, null, cancellationToken)).SingleOrDefault() ?? throw new PersonConflictException("not_found");
        var mask = !actor.Roles.Any(role => role is RoleCode.Hr or RoleCode.Owner);
        var checklist = PersonRules.Checklist(facts, today, mask);
        var visible = checklist.Where(item => !item.Masked).ToArray();
        return new(PersonRules.Row(facts, today, mask), PersonRules.Fields(facts.Employee), facts.Employee.DepotId,
            checklist, visible.Count(PersonRules.Valid), visible.Length);
    }
}
