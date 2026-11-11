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

public sealed class DriverSnapshotQueryHandler(FleetOpsDbContext db, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<GetDriverSnapshotQuery, DriverSnapshot>
{
    public async Task<DriverSnapshot> HandleAsync(GetDriverSnapshotQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var generatedAt = time.GetUtcNow();
        var today = companyTime.ToCompanyDate(generatedAt);
        var horizon = today.AddDays(84);
        var cycles = await db.Cycles.AsNoTracking().Where(cycle => cycle.EmployeeId == actor.EmployeeId && (cycle.To >= today.AddDays(-28) || cycle.Status == CycleStatus.Overrun) && cycle.From < horizon)
            .OrderBy(cycle => cycle.From).Select(cycle => new DriverCycle(cycle.Id, cycle.VehicleId, db.Vehicles.Where(vehicle => vehicle.Id == cycle.VehicleId).Select(vehicle => vehicle.Plate).FirstOrDefault() ?? "", cycle.From, cycle.To, cycle.Status, cycle.Source, cycle.Version)).ToListAsync(cancellationToken);
        var documents = await db.Documents.AsNoTracking().Where(document => document.OwnerType == DocumentOwnerType.Employee && document.OwnerId == actor.EmployeeId && document.Status != DocumentStatus.Replaced)
            .OrderBy(document => document.Type).ThenByDescending(document => document.CreatedAt).Select(document => new DriverDocument(document.Id, document.Type, document.Status, document.ExpiresAt, document.RejectReason)).ToListAsync(cancellationToken);
        var readiness = await new EfReadinessStore(db).ForDriversAsync([actor.EmployeeId], today, cancellationToken);
        var own = readiness.GetValueOrDefault(actor.EmployeeId) ?? new DriverReadiness(false, []);
        var defects = await db.DefectReports.AsNoTracking().Where(defect => defect.ReportedBy == actor.EmployeeId).OrderByDescending(defect => defect.ReportedAt).ToListAsync(cancellationToken);
        return new(actor.EmployeeId, generatedAt, cycles, documents, own.CanDrive, own.Causes)
        {
            Defects = defects.Select(defect => new DriverOwnDefect(defect.Id, defect.VehicleId, System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(defect.Category.ToString()), System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(defect.Severity.ToString()), System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(defect.Status.ToString()), defect.ReportedAt)).ToArray()
        };
    }
}

