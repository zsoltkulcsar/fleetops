using System.Text;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Audit;
public sealed class AuditLogHandler(IAuditLogStore store) : IQueryHandler<GetAuditLogQuery, AuditPage>, ICommandHandler<ExportAuditLogCommand, string>
{
    public async Task<AuditPage> HandleAsync(GetAuditLogQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        Validate(query.Filter);
        return Mask(await store.ReadAsync(Normalize(query.Filter), null, cancellationToken));
    }
    public async Task<string> HandleAsync(ExportAuditLogCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        Validate(command.Filter);
        var csv = new StringBuilder("time,who,entity,entityId,field,old,new,reason\r\n");
        var pageNumber = 1;
        AuditPage page;
        do
        {
            page = Mask(await store.ReadAsync(Normalize(command.Filter) with { Page = pageNumber, PageSize = 100 }, context, cancellationToken));
            foreach (var row in page.Items)
                csv.AppendLine(string.Join(",", new[] { row.Time.ToString("O"), row.Who, row.EntityType, row.EntityId.ToString(), row.Field, row.OldValue, row.NewValue, row.Reason }.Select(Cell)));
            pageNumber++;
        } while ((pageNumber - 1L) * 100 < page.Total);
        await store.RecordExportAsync(actor.EmployeeId, context, cancellationToken);
        return csv.ToString();
    }
    private static AuditPage Mask(AuditSourcePage page) => new(page.Items.Select(source => source.Special
        ? source.Row with { OldValue = "changed", NewValue = "changed", Reason = "changed" }
        : source.Row).ToArray(), page.Total, page.Page, page.PageSize);
    private static string Cell(string? value)
    {
        var text = value ?? "";
        if (text.Length > 0 && "=+-@\t\r\n".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private static AuditFilter Normalize(AuditFilter filter) => filter with
    {
        From = filter.From?.ToUniversalTime(),
        To = filter.To?.ToUniversalTime()
    };
    private static void Validate(AuditFilter filter)
    {
        var fields = new List<string>();
        if (filter.Page < 1) fields.Add("page");
        if (filter.PageSize < 1 || filter.PageSize > 100) fields.Add("pageSize");
        if (filter.From > filter.To)
        {
            fields.Add("from");
            fields.Add("to");
        }
        if (filter.EntityType?.Length > 100) fields.Add("entityType");
        if (fields.Count > 0) throw new AuditFilterException(fields);
    }
}

