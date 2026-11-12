using FleetOps.Kernel;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Maintenance;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Readiness;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class OwnerOverviewStore(IConfiguration configuration, TimeProvider time, ICompanyTimeService companyTime) : IOwnerOverviewStore
{
    public async Task<OwnerOverviewFacts> ReadAsync(CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration);
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var month = new DateOnly(today.Year, today.Month, 1);
        var previousMonth = month.AddMonths(-1);
        var critical = await db.Alerts.CountAsync(alert => alert.Status != AlertStatus.Resolved && alert.Severity == AlertSeverity.Critical, cancellationToken);
        var vehicles = await db.Vehicles.AsNoTracking().Where(vehicle => vehicle.Status == VehicleStatus.Active).ToListAsync(cancellationToken);
        var readiness = await new EfReadinessStore(db, time).ForVehiclesAsync(vehicles.Where(vehicle => vehicle.Kind == VehicleKind.Truck).Select(vehicle => vehicle.Id).ToArray(), today, cancellationToken);
        var drivers = await db.Employees.AsNoTracking().Where(employee => employee.Status == EmployeeStatus.Active && employee.Roles.Contains("driver") && (employee.EndDate == null || employee.EndDate >= today)).Select(employee => employee.Id).ToListAsync(cancellationToken);
        var verified = await db.Documents.AsNoTracking().Where(document => document.Status == DocumentStatus.Verified && (document.ExpiresAt == null || document.ExpiresAt >= today) && (document.IssuedAt == null || document.IssuedAt <= today)).Select(document => new { document.OwnerType, document.OwnerId, document.Type }).ToListAsync(cancellationToken);
        var exportStatus = await db.PayrollExports.AsNoTracking().Where(export => export.PeriodFrom == previousMonth && export.PeriodTo == month.AddDays(-1)).OrderByDescending(export => export.CreatedAt).Select(export => export.Status).FirstOrDefaultAsync(cancellationToken) ?? "not_generated";
        var cost = await db.Maintenance.Where(repair => repair.Status == MaintenanceStatus.Done && repair.DoneAt >= month && repair.DoneAt < month.AddMonths(1)).SumAsync(repair => repair.CostEur ?? 0, cancellationToken);
        return new(critical,
            vehicles.Select(vehicle => new OwnerVehicleFact(vehicle.Id, vehicle.Kind, vehicle.GrossWeightKg, readiness.TryGetValue(vehicle.Id, out var state) && state.ShownStatus == ReadinessShownStatus.OffRoad)).ToArray(),
            drivers,
            verified.Select(document => new OwnerDocumentFact(document.OwnerId, document.OwnerType == DocumentOwnerType.Employee, document.Type.ToString())).ToArray(),
            exportStatus, cost);
    }
}
