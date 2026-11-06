using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Readiness;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class ReadinessSummaryQueryHandler(FleetOpsDbContext db, IReadiness readiness, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<GetReadinessSummaryQuery, ReadinessSummary>
{
    public async Task<ReadinessSummary> HandleAsync(GetReadinessSummaryQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var vehicles = await db.Vehicles.AsNoTracking().Where(x => x.Status == VehicleStatus.Active).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var vehicleReadiness = await readiness.ForVehiclesAsync(vehicles, companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken);
        var employees = await db.Employees.AsNoTracking().Where(x => x.Status != EmployeeStatus.Left && x.Roles.Contains("driver")).Select(x => x.Id).ToArrayAsync(cancellationToken);
        var driverReadiness = await readiness.ForDriversAsync(employees, companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken);
        return new(vehicleReadiness.Values.Count(x => x.Usable), vehicles.Length, driverReadiness.Values.Count(x => x.CanDrive), employees.Length);
    }
}

public sealed class DriverReadinessQueryHandler(FleetOpsDbContext db, IReadiness readiness, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<ListDriverReadinessQuery, DriverReadinessList>
{
    public async Task<DriverReadinessList> HandleAsync(ListDriverReadinessQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var employees = await db.Employees.AsNoTracking().Where(x => x.Status != EmployeeStatus.Left && x.Roles.Contains("driver")).OrderBy(x => x.LastName).ThenBy(x => x.FirstName).ToListAsync(cancellationToken);
        var values = await readiness.ForDriversAsync(employees.Select(x => x.Id).ToArray(), companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken);
        var items = employees.Select(employee => new DriverReadinessItem(employee.Id, employee.FirstName + " " + employee.LastName, values[employee.Id].CanDrive, Mask(values[employee.Id].Causes, actor))).Where(x => !query.BlockedOnly || !x.CanDrive).Take(Math.Clamp(query.Limit, 1, 100)).ToList();
        return new(items, null);
    }

    private static IReadOnlyList<ReadinessCause> Mask(IReadOnlyList<ReadinessCause> causes, AccessContext actor)
    {
        var medical = causes.FirstOrDefault(x => string.Equals(x.DocumentType, "medical", StringComparison.Ordinal));
        if (!actor.Roles.Contains(RoleCode.Dispatcher) || medical is null) return causes;
        return [new(medical.Code, null)];
    }
}
