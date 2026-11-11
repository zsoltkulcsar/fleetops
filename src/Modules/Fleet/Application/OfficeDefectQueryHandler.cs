using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Fleet;

public sealed class OfficeDefectQueryHandler(IOfficeDefectReads reads, TimeProvider time) :
    IQueryHandler<ListDefectsQuery, DefectListResult>, IQueryHandler<GetDefectQuery, DefectDetail>
{
    public Task<DefectListResult> HandleAsync(ListDefectsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var fields = new List<string>();
        if (query.Page < 1 || query.Page > int.MaxValue / 100) fields.Add("page");
        if (query.PageSize is < 1 or > 100) fields.Add("pageSize");
        if (query.StatusGroup is not null && query.StatusGroup is not ("open" or "in_repair" or "closed")) fields.Add("statusGroup");
        if (query.Severity.HasValue && !Enum.IsDefined(query.Severity.Value)) fields.Add("severity");
        if (query.VehicleId == Guid.Empty) fields.Add("vehicleId");
        if (fields.Count > 0) throw new VehicleValidationException(fields);
        return reads.ListAsync(query, time.GetUtcNow(), cancellationToken);
    }
    public Task<DefectDetail> HandleAsync(GetDefectQuery query, AccessContext actor, CancellationToken cancellationToken)
        => reads.DetailAsync(query.Id, cancellationToken);
}
