using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Migrations;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Views;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class DriverDefectPhotoLinkQueryHandler(FleetOpsDbContext db, IObjectStore objects, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<GetDriverDefectPhotoLinkQuery, DriverFileLinkResult>
{
    public async Task<DriverFileLinkResult> HandleAsync(GetDriverDefectPhotoLinkQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var defect = await db.DefectReports.AsNoTracking().Where(defect => defect.Id == query.DefectId).Select(defect => new { defect.VehicleId, defect.ReportedBy }).SingleOrDefaultAsync(cancellationToken);
        if (defect is null || (defect.ReportedBy != actor.EmployeeId && !await DriverAccess.HasTruckCycle(db, actor.EmployeeId, defect.VehicleId, companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken)))
            throw new DocumentConflictException("not_found");
        var key = await db.DefectPhotos.Where(photo => photo.DefectId == query.DefectId && photo.Id == query.FileId).Select(photo => photo.ObjectKey).SingleOrDefaultAsync(cancellationToken) ?? throw new DocumentConflictException("not_found");
        var link = await objects.CreateDownloadLinkAsync(key, TimeSpan.FromMinutes(5), cancellationToken);
        return new(link.Url, link.ExpiresAt);
    }
}
