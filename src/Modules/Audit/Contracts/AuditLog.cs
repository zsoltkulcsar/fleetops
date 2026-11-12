using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Audit;
public sealed record AuditFilter(Guid? PersonId = null, string? EntityType = null, Guid? EntityId = null, DateTimeOffset? From = null, DateTimeOffset? To = null, int Page = 1, int PageSize = 50);
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record AuditRow(Guid Id, DateTimeOffset Time, string Who, string EntityType, Guid EntityId, string Field, string? OldValue, string? NewValue, string Reason);
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record AuditPage(IReadOnlyList<AuditRow> Items, int Total, int Page, int PageSize) : IReadModel;
[QueryPolicy("audit.view")]
public sealed record GetAuditLogQuery(AuditFilter Filter) : IQuery<AuditPage>;
[CommandPolicy("audit.export")]
public sealed record ExportAuditLogCommand(AuditFilter Filter) : ICommand<string>;
public sealed record AuditSourceRow(AuditRow Row, bool Special);
public sealed record AuditSourcePage(IReadOnlyList<AuditSourceRow> Items, int Total, int Page, int PageSize);
public interface IAuditLogStore
{
    Task<AuditSourcePage> ReadAsync(AuditFilter filter, CommandContext? context, CancellationToken cancellationToken);
    Task RecordExportAsync(Guid actorId, CommandContext context, CancellationToken cancellationToken);
}

