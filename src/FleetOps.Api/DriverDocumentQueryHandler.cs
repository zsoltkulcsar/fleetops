using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Views;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class DriverDocumentQueryHandler(FleetOpsDbContext db, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<GetDriverDocumentQuery, DriverDocumentDetail>
{
    public async Task<DriverDocumentDetail> HandleAsync(GetDriverDocumentQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var document = await DriverAccess.DocumentAsync(db, actor.EmployeeId, query.DocumentId, false, companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken);
        return new(document.Id, document.Type, document.Status, document.ExpiresAt, document.RejectReason, await DriverAccess.Files(db, document.Id, cancellationToken));
    }
}

