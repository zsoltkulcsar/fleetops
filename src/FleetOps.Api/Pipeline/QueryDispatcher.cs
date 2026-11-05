using FleetOps.Kernel.Authorization;

namespace FleetOps.Api.Pipeline;

public sealed class QueryDispatcher(IAccessContextAccessor accessor, IServiceProvider services)
{
    public async Task<TResult> DispatchAsync<TQuery, TResult>(TQuery query, CancellationToken cancellationToken = default)
        where TQuery : IQuery<TResult> where TResult : IReadModel
    {
        var attribute = query.GetType().GetCustomAttributes(typeof(QueryPolicyAttribute), true).SingleOrDefault() as QueryPolicyAttribute;
        var actionKey = attribute?.ActionKey ?? "query";
        var actor = accessor.Current;
        if (actor is null) throw new AuthorizationException(RefusalCodes.NotSignedIn, actionKey, []);
        var entry = attribute is null ? null : PermissionMatrix.Entries.SingleOrDefault(x => x.ActionKey == actionKey);
        var subject = (query as IHasSubject)?.SubjectId;
        var readableRoles = actor.Roles.Where(role =>
        {
            var cell = entry?.Cells.GetValueOrDefault(role) ?? PermissionMatrix.Cell.None;
            return cell is not PermissionMatrix.Cell.None && (cell != PermissionMatrix.Cell.Own || subject is Guid own && own == actor.EmployeeId);
        }).ToHashSet();
        if (entry is null || readableRoles.Count == 0)
            throw new AuthorizationException(RefusalCodes.PermissionDenied, actionKey, entry?.ReadableRoles ?? new HashSet<RoleCode>());
        var visible = typeof(TResult).GetCustomAttributes(typeof(VisibleToAttribute), true).SingleOrDefault() as VisibleToAttribute;
        var visibleRoles = readableRoles.Where(role => visible?.Roles.Contains(role) == true).ToHashSet();
        if (visible is null || visibleRoles.Count == 0)
            throw new AuthorizationException(RefusalCodes.PermissionDenied, actionKey, entry.ReadableRoles);
        var handler = services.GetService<IQueryHandler<TQuery, TResult>>() ?? throw new InvalidOperationException($"No query handler registered for {typeof(TQuery).FullName}.");
        var result = await handler.HandleAsync(query, actor, cancellationToken);
        if (result is not null && result.GetType() != typeof(TResult)) throw new InvalidOperationException($"Handler returned {result.GetType()} instead of {typeof(TResult)}.");
        return result!;
    }
}
