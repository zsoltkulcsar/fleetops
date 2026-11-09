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

public sealed class DriverFileLinkQueryHandler(FleetOpsDbContext db, IObjectStore objects, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<GetDriverFileLinkQuery, DriverFileLinkResult>
{
    public async Task<DriverFileLinkResult> HandleAsync(GetDriverFileLinkQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        await DriverAccess.DocumentAsync(db, actor.EmployeeId, query.DocumentId, true, companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken);
        var key = await db.DocumentFiles.AsNoTracking().Where(file => file.DocumentId == query.DocumentId && file.Id == query.FileId).Select(file => file.ObjectKey).SingleOrDefaultAsync(cancellationToken) ?? throw new DocumentConflictException("not_found");
        var link = await objects.CreateDownloadLinkAsync(key, TimeSpan.FromMinutes(5), cancellationToken);
        return new(link.Url, link.ExpiresAt);
    }
}

