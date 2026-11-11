using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfDefectNotificationFacts(ICompanyTimeService companyTime, TimeProvider time) : IDefectNotificationFacts
{
    public async Task<IReadOnlyList<Guid>> DriversAsync(Guid vehicleId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var drivers = db.Shifts.Where(shift => shift.TruckId == vehicleId && shift.Status == ShiftStatus.Running).Select(shift => shift.EmployeeId)
            .Union(db.Assignments.Where(assignment => assignment.VehicleId == vehicleId && assignment.From <= today && (assignment.To == null || assignment.To >= today)).Select(assignment => assignment.EmployeeId))
            .Union(db.Cycles.Where(cycle => cycle.VehicleId == vehicleId && cycle.From <= today && (cycle.To >= today || cycle.Status == CycleStatus.Overrun) && cycle.Status != CycleStatus.Cancelled && cycle.Status != CycleStatus.Done).Select(cycle => cycle.EmployeeId));
        return await db.Employees.Where(employee => drivers.Contains(employee.Id) && employee.Status == EmployeeStatus.Active && employee.Roles.Contains("\"driver\"")).Select(employee => employee.Id).ToListAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<Guid>> DispatchersAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        return await db.Employees.Where(employee => employee.Status == EmployeeStatus.Active && employee.Roles.Contains("\"dispatcher\"")).Select(employee => employee.Id).ToListAsync(cancellationToken);
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
