using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfShiftStartFacts(ICompanyTimeService companyTime) : IShiftStartFacts
{
    public async Task<ShiftStartFacts> ReadAsync(Guid employeeId, Guid? truckId, Guid? trailerId, DateTimeOffset startAt, CommandContext context, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        await using var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        var date = companyTime.ToCompanyDate(startAt);
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == employeeId, cancellationToken);
        var suggested = await ResolveTruckForShift(db, employeeId, date, cancellationToken);
        var chosen = truckId ?? suggested;
        var readiness = new EfReadinessStore(db);
        var driver = await readiness.ForDriverAtAsync(employeeId, date, startAt, cancellationToken);
        var ids = new[] { chosen, trailerId }.Where(id => id.HasValue).Select(id => id!.Value).ToArray();
        var vehicles = await readiness.ForVehiclesAtAsync(ids, date, startAt, cancellationToken);
        var truck = chosen is Guid chosenId ? await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(vehicle => vehicle.Id == chosenId, cancellationToken) : null;
        var trailer = trailerId is Guid attachedId ? await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(vehicle => vehicle.Id == attachedId, cancellationToken) : null;
        if ((chosen is not null && truck?.Kind != VehicleKind.Truck) || (trailerId is not null && trailer?.Kind != VehicleKind.Trailer))
            throw new ShiftRuleException("invalid_vehicle");
        return new(employee is { Status: EmployeeStatus.Active } && employee.StartDate <= date && (employee.EndDate is null || employee.EndDate >= date),
            driver?.CanDrive == true, suggested,
            chosen is null || (truck?.Kind == VehicleKind.Truck && vehicles.GetValueOrDefault(chosen.Value)?.Usable == true),
            trailerId is null || (trailer?.Kind == VehicleKind.Trailer && vehicles.GetValueOrDefault(trailerId.Value)?.Usable == true))
        {
            OverrideId = chosen is null ? null : await db.VehicleOverrides.Where(release => release.VehicleId == chosen && release.At <= startAt && release.ValidUntil >= startAt).OrderByDescending(release => release.At).Select(release => (Guid?)release.Id).FirstOrDefaultAsync(cancellationToken),
            EmployeeStatus = employee?.Status.ToString().ToLowerInvariant() ?? "missing"
        };
    }

    public static async Task<Guid?> ResolveTruckForShift(FleetOpsDbContext db, Guid employeeId, DateOnly date, CancellationToken cancellationToken)
    {
        var swap = await db.Assignments.AsNoTracking().Where(assignment => assignment.EmployeeId == employeeId && assignment.From <= date &&
            (assignment.To == null || assignment.To >= date) && db.Vehicles.Any(vehicle => vehicle.Id == assignment.VehicleId && vehicle.Kind == VehicleKind.Truck))
            .OrderByDescending(assignment => assignment.From).ThenByDescending(assignment => assignment.Id).Select(assignment => (Guid?)assignment.VehicleId).FirstOrDefaultAsync(cancellationToken);
        if (swap is not null) return swap;
        return await db.Cycles.AsNoTracking().Where(cycle => cycle.EmployeeId == employeeId && cycle.From <= date &&
            (cycle.To >= date || cycle.Status == CycleStatus.Overrun) && cycle.Status != CycleStatus.Cancelled && cycle.Status != CycleStatus.Done)
            .OrderByDescending(cycle => cycle.From).Select(cycle => (Guid?)cycle.VehicleId).FirstOrDefaultAsync(cancellationToken);
    }
}
