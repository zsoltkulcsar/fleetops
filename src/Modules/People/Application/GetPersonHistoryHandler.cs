using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

public sealed class GetPersonHistoryHandler(IPersonHistoryStore store) : IQueryHandler<GetPersonHistoryQuery, PersonHistory>
{
    public async Task<PersonHistory> HandleAsync(GetPersonHistoryQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var history = await store.ReadAsync(query.SubjectId ?? Guid.Empty, cancellationToken);
        if (actor.Roles.Any(role => role is RoleCode.Hr or RoleCode.Owner)) return new(history.Items.Select(entry => entry with { Masked = false }).ToArray());
        return new(history.Items.Select(entry => entry.Masked || entry.Field.Contains("medical", StringComparison.OrdinalIgnoreCase)
            ? entry with { OldValue = null, NewValue = null, Reason = "Medical - visible to HR only", Masked = true }
            : entry).ToArray());
    }
}
