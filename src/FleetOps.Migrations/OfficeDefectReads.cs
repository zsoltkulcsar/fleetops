using FleetOps.Kernel.Files;
using FleetOps.Modules.Fleet;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfOfficeDefectReads(FleetOpsDbContext db, IObjectStore objects, TimeProvider time) : IOfficeDefectReads
{
    public async Task<DefectListResult> ListAsync(ListDefectsQuery query, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var reports = db.DefectReports.AsNoTracking().AsQueryable();
        if (query.VehicleId.HasValue) reports = reports.Where(defect => defect.VehicleId == query.VehicleId);
        if (query.Severity.HasValue) reports = reports.Where(defect => defect.Severity == query.Severity);
        var open = reports.Where(defect => defect.Status == DefectStatus.Open || defect.Status == DefectStatus.Acknowledged || defect.Status == DefectStatus.InRepair);
        var counts = new DefectOriginCounts(
            await open.CountAsync(defect => defect.Origin == DefectOrigin.App, cancellationToken),
            await open.CountAsync(defect => defect.Origin == DefectOrigin.OfficeFromChat, cancellationToken));
        reports = query.StatusGroup switch
        {
            "open" => reports.Where(defect => defect.Status == DefectStatus.Open || defect.Status == DefectStatus.Acknowledged),
            "in_repair" => reports.Where(defect => defect.Status == DefectStatus.InRepair),
            "closed" => reports.Where(defect => defect.Status == DefectStatus.Fixed || defect.Status == DefectStatus.Closed || defect.Status == DefectStatus.Cancelled),
            _ => reports
        };
        var total = await reports.CountAsync(cancellationToken);
        var page = reports.OrderBy(defect => defect.Status == DefectStatus.Fixed || defect.Status == DefectStatus.Closed || defect.Status == DefectStatus.Cancelled)
            .ThenByDescending(defect => defect.ReportedAt).ThenBy(defect => defect.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize);
        var rows = await Rows(page).ToListAsync(cancellationToken);
        return new(rows.Select(row => WithAge(row, now)).ToArray(), query.Page, query.PageSize, total, counts);
    }

    public async Task<DefectDetail> DetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var reports = db.DefectReports.AsNoTracking().Where(defect => defect.Id == id);
        var report = await reports.SingleOrDefaultAsync(cancellationToken) ?? throw new VehicleConflictException("not_found");
        var row = await Rows(reports).SingleAsync(cancellationToken);
        var files = await db.DefectPhotos.AsNoTracking().Where(photo => photo.DefectId == id).OrderBy(photo => photo.SortOrder).ToListAsync(cancellationToken);
        var photos = new List<DefectPhotoLink>();
        foreach (var file in files)
            photos.Add(new(file.Id, await objects.CreateDownloadLinkAsync(file.ObjectKey, TimeSpan.FromMinutes(5), cancellationToken)));
        var history = await db.AuditEntries.AsNoTracking().Where(entry => entry.EntityType == nameof(DefectReport) && entry.EntityId == id)
            .OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Id)
            .Select(entry => new DefectHistoryItem(entry.FieldName, entry.OldValue, entry.NewValue, entry.ActorId, entry.Reason, entry.CreatedAt)).ToListAsync(cancellationToken);
        var readable = history.Select(item => DefectHistoryText.Readable(item, files.Count)).Where(item => item is not null).Select(item => item!).ToList();
        return new(WithAge(row, time.GetUtcNow()), report.Description, report.RepairId, report.ClosedBy, report.ClosedAt, report.CloseReason, photos, readable);
    }

    private IQueryable<DefectRow> Rows(IQueryable<DefectReport> reports)
        => from defect in reports
           join vehicle in db.Vehicles on defect.VehicleId equals vehicle.Id
           join employee in db.Employees on defect.ReportedBy equals employee.Id
           select new DefectRow(defect.Id, defect.VehicleId, vehicle.Plate, defect.Category, defect.Severity,
               defect.ReportedBy, employee.FirstName + " " + employee.LastName, defect.ReportedAt,
               db.DefectPhotos.Count(photo => photo.DefectId == defect.Id), defect.Status, 0, defect.Origin);

    private static DefectRow WithAge(DefectRow row, DateTimeOffset now) => row with { AgeDays = Math.Max(0, (now - row.ReportedAt).TotalDays) };
}
