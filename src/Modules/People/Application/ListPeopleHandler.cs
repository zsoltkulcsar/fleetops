using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class ListPeopleHandler(IPeopleStore store, ICompanyTimeService clock, TimeProvider time) : IQueryHandler<ListPeopleQuery, PeoplePage>
{
    public async Task<PeoplePage> HandleAsync(ListPeopleQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.Page < 1 || query.Page > 1000000) throw new PersonValidationException(["page"]);
        if (query.PageSize is < 1 or > 100) throw new PersonValidationException(["pageSize"]);
        if (query.Role is not null && !PersonRules.Roles.Contains(query.Role)) throw new PersonValidationException(["role"]);
        if (query.Status is not null && query.Status is not ("onboarding" or "active" or "suspended" or "left")) throw new PersonValidationException(["status"]);
        if (query.Search?.Length > 200) throw new PersonValidationException(["search"]);
        var today = clock.ToCompanyDate(time.GetUtcNow());
        var mask = !actor.Roles.Any(role => role is RoleCode.Hr or RoleCode.Owner);
        var facts = await store.ReadAsync(today, null, null, cancellationToken);
        var rows = facts.Where(person => !query.ExpiringWithin30 || PersonRules.Checklist(person, today, mask).Any(item => item.State == "expiring"))
            .Select(person => PersonRules.Row(person, today, mask))
            .Where(row => query.Role is null || row.Roles.Contains(query.Role))
            .Where(row => query.Status is null || row.Status == query.Status)
            .Where(row => !query.BlockedOnly || row.Roles.Contains("driver") && !row.CanDrive)
            .Where(row => string.IsNullOrWhiteSpace(query.Search) || row.Name.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase)
                || facts.Any(person => person.Employee.Id == row.Id && person.Employee.PersonnelNumber?.Contains(query.Search.Trim(), StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.Id).ToArray();
        return new(rows.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray(), query.Page, query.PageSize, rows.Length);
    }
}
