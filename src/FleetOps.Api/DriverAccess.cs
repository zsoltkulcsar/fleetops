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

public static class DriverAccess
{
    public static Task<bool> HasTruckCycle(FleetOpsDbContext db, Guid employeeId, Guid vehicleId, DateOnly today, CancellationToken cancellationToken) => db.Cycles.AsNoTracking().AnyAsync(cycle => cycle.EmployeeId == employeeId && cycle.VehicleId == vehicleId && cycle.Status != CycleStatus.Cancelled && cycle.Status != CycleStatus.Done && (cycle.To >= today || cycle.Status == CycleStatus.Overrun), cancellationToken);
    public static async Task<Document> DocumentAsync(FleetOpsDbContext db, Guid employeeId, Guid documentId, bool allowVehicle, DateOnly today, CancellationToken cancellationToken)
    {
        var document = await db.Documents.AsNoTracking().SingleOrDefaultAsync(document => document.Id == documentId, cancellationToken);
        if (document is null || !(document.OwnerType == DocumentOwnerType.Employee && document.OwnerId == employeeId || allowVehicle && document.OwnerType == DocumentOwnerType.Vehicle && await HasTruckCycle(db, employeeId, document.OwnerId, today, cancellationToken)))
            throw new DocumentConflictException("not_found");
        return document;
    }
    public static Task<List<DriverFile>> Files(FleetOpsDbContext db, Guid documentId, CancellationToken cancellationToken) => db.DocumentFiles.AsNoTracking().Where(file => file.DocumentId == documentId).OrderBy(file => file.SortOrder).Select(file => new DriverFile(file.Id, file.Page, file.ContentType, file.SizeBytes)).ToListAsync(cancellationToken);
}

