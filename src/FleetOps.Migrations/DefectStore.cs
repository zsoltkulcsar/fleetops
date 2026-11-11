using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Modules.Fleet;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfDefectStore(IObjectStore objects, ICompanyTimeService companyTime, TimeProvider time) : IDefectStore, IOfficeDefectStore
{
    public async Task LockVehicleAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM fleetops.vehicles WHERE id = {vehicleId} FOR UPDATE", cancellationToken);
    }

    public async Task<DefectReport?> LoadAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        return await db.DefectReports.AsNoTracking().SingleOrDefaultAsync(defect => defect.Id == id, cancellationToken);
    }

    public Task ValidateReportAsync(DefectReport report, CommandContext context, CancellationToken cancellationToken) => ValidatePhotosAsync(report, report.ReportedBy, context, cancellationToken);
    public async Task ValidatePhotosAsync(DefectReport report, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        foreach (var key in new[] { $"defect-id:{report.Id}", $"defect-command:{report.CommandId}" })
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        if (await db.DefectReports.AnyAsync(defect => defect.Id == report.Id || (report.CommandId != null && defect.CommandId == report.CommandId), cancellationToken))
            throw new VehicleConflictException("command_id_reused");
        if (!await db.Vehicles.AnyAsync(vehicle => vehicle.Id == report.VehicleId, cancellationToken))
            throw new VehicleValidationException(["vehicleId"]);
        foreach (var fileId in report.Photos.Order())
        {
            var key = $"pending-file:{fileId}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        }
        var files = await db.PendingFiles.FromSqlInterpolated($"SELECT * FROM fleetops.pending_files WHERE id = ANY({report.Photos}) ORDER BY id FOR UPDATE").ToListAsync(cancellationToken);
        if (files.Count != report.Photos.Length) throw new VehicleValidationException(["photos"]);
        foreach (var file in files)
        {
            if (file.ActorId != actorId || file.ConfirmedAt != null || file.ExpiresAt <= time.GetUtcNow() || file.Purpose != "defect_photo")
                throw new VehicleValidationException(["photos"]);
            var info = await objects.GetInfoAsync(file.ObjectKey, cancellationToken);
            if (info is null || info.ContentType != file.ContentType || info.SizeBytes < 1 || info.SizeBytes > file.MaxSizeBytes)
                throw new VehicleValidationException(["photos"]);
        }
    }

    public Task SaveAsync(DefectReport report, Guid actorId, CommandContext context, CancellationToken cancellationToken) => SaveAsync(report, actorId, report.Status == DefectStatus.Cancelled ? "Cancelled by driver within 60 s" : "driver_defect_report", context, cancellationToken);
    public async Task SaveAsync(DefectReport report, Guid actorId, string reason, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        db.AuditActorId = actorId;
        db.AuditReason = reason;
        var existing = await db.DefectReports.SingleOrDefaultAsync(defect => defect.Id == report.Id, cancellationToken);
        if (existing is null)
        {
            db.DefectReports.Add(report);
            var files = await db.PendingFiles.Where(file => report.Photos.Contains(file.Id)).ToListAsync(cancellationToken);
            foreach (var file in files)
            {
                var info = await objects.GetInfoAsync(file.ObjectKey, cancellationToken) ?? throw new VehicleValidationException(["photos"]);
                file.ConfirmedAt = time.GetUtcNow();
                db.DefectPhotos.Add(new DefectPhoto { Id = file.Id, DefectId = report.Id, ObjectKey = file.ObjectKey, ContentType = file.ContentType, SizeBytes = info.SizeBytes, SortOrder = Array.IndexOf(report.Photos, file.Id) });
            }
        }
        else db.Entry(existing).CurrentValues.SetValues(report);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> IsOffRoadAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        var readiness = await new EfReadinessStore(db).ForVehiclesAsync([vehicleId], companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken);
        return !readiness.TryGetValue(vehicleId, out var vehicle) || vehicle.ShownStatus is ReadinessShownStatus.OffRoad or ReadinessShownStatus.Retired;
    }

    public async Task ValidateReporterAsync(Guid reporterId, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        if (!await db.Employees.AnyAsync(employee => employee.Id == reporterId && employee.Status == FleetOps.People.EmployeeStatus.Active && (employee.Id == actorId || employee.Roles.Contains("\"driver\"")), cancellationToken))
            throw new VehicleValidationException(["reportedBy"]);
    }

    private static FleetOpsDbContext Create(CommandContext context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
}
