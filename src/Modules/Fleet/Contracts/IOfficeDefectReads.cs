using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Fleet;

public interface IOfficeDefectReads
{
    Task<DefectListResult> ListAsync(ListDefectsQuery query, DateTimeOffset now, CancellationToken cancellationToken);
    Task<DefectDetail> DetailAsync(Guid id, CancellationToken cancellationToken);
}
