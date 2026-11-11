using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
public sealed class GetLeavePreviewHandler(ILeaveStore store, ICompanyTimeService clock, TimeProvider time) : IQueryHandler<GetLeavePreviewQuery, LeavePreview>
{
    public Task<LeavePreview> HandleAsync(GetLeavePreviewQuery query, AccessContext actor, CancellationToken cancellationToken) =>
        store.PreviewAsync(query.SubjectId ?? Guid.Empty, query.EndDate ?? clock.ToCompanyDate(time.GetUtcNow()), null, cancellationToken);
}
