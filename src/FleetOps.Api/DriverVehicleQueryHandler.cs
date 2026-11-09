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

public sealed class DriverVehicleQueryHandler(FleetOpsDbContext db, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<GetDriverVehicleQuery, DriverVehicleDetail>
{
    public async Task<DriverVehicleDetail> HandleAsync(GetDriverVehicleQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (!await DriverAccess.HasTruckCycle(db, actor.EmployeeId, query.VehicleId, companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken)) throw new DocumentConflictException("not_found");
        var plate = await db.Vehicles.Where(vehicle => vehicle.Id == query.VehicleId).Select(vehicle => vehicle.Plate).SingleOrDefaultAsync(cancellationToken) ?? throw new DocumentConflictException("not_found");
        var documents = await db.Documents.AsNoTracking().Where(document => document.OwnerType == DocumentOwnerType.Vehicle && document.OwnerId == query.VehicleId && document.Status != DocumentStatus.Replaced).OrderBy(document => document.Type).ToListAsync(cancellationToken);
        var resultDocuments = new List<DriverVehicleDocument>();
        var items = new List<DriverVehicleItem>();
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        foreach (var document in documents)
        {
            var files = await DriverAccess.Files(db, document.Id, cancellationToken);
            resultDocuments.Add(new(document.Id, document.Type, document.Status, document.ExpiresAt, files));
            if (document.Status == DocumentStatus.Uploaded || document.Status == DocumentStatus.Rejected || document.ExpiresAt < today)
                items.Add(new(document.Id, "document", document.ExpiresAt < today && DocumentsCatalog.Get(document.Type).Blocking ? "critical" : "warning", document.ExpiresAt ?? DateOnly.FromDateTime(document.CreatedAt.DateTime), document.Id, files.FirstOrDefault()?.Id));
        }
        var vehicleDocumentIds = documents.Select(document => document.Id).ToArray();
        var alerts = await db.Alerts.AsNoTracking().Where(alert => alert.Status != AlertStatus.Resolved && (alert.SubjectType == "vehicle" && alert.SubjectId == query.VehicleId || alert.SubjectType == "document" && vehicleDocumentIds.Contains(alert.SubjectId))).ToListAsync(cancellationToken);
        items.AddRange(alerts.Select(alert => new DriverVehicleItem(alert.Id, alert.RuleId, alert.Severity.ToString().ToLowerInvariant(), alert.DueAt ?? DateOnly.FromDateTime(alert.OpenedAt.DateTime), alert.SubjectType == "document" ? alert.SubjectId : null, null)));
        return new(query.VehicleId, plate, items, resultDocuments);
    }
}
