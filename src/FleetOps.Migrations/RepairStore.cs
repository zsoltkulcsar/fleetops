using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Maintenance;
using FleetOps.Modules.Schedule;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;

public sealed class EfRepairStore(FleetOpsDbContext reads) : IRepairStore
{
    public async Task LockAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(540055)", cancellationToken);
    }
    public async Task<Maintenance?> LoadRepairAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        return await db.Maintenance.AsNoTracking().SingleOrDefaultAsync(repair => repair.Id == id, cancellationToken);
    }
    public async Task SaveRepairAsync(Maintenance repair, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        db.AuditActorId = actorId;
        db.AuditReason = "repair_changed";
        var existing = await db.Maintenance.SingleOrDefaultAsync(row => row.Id == repair.Id, cancellationToken);
        if (existing is null) db.Maintenance.Add(repair);
        else db.Entry(existing).CurrentValues.SetValues(repair);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<Assignment?> LoadAssignmentAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        return await db.Assignments.AsNoTracking().SingleOrDefaultAsync(assignment => assignment.Id == id, cancellationToken);
    }
    public async Task SaveAssignmentAsync(Assignment assignment, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        db.AuditActorId = actorId;
        db.AuditReason = "assignment_changed";
        var existing = await db.Assignments.SingleOrDefaultAsync(row => row.Id == assignment.Id, cancellationToken);
        if (existing is null) db.Assignments.Add(assignment);
        else db.Entry(existing).CurrentValues.SetValues(assignment);
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<Assignment>> LinkedAssignmentsAsync(Guid repairId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        return await db.Assignments.AsNoTracking().Where(assignment => assignment.MaintenanceId == repairId).ToListAsync(cancellationToken);
    }
    public async Task<bool> IsActiveDriverAsync(Guid id, DateOnly date, CommandContext? context, CancellationToken cancellationToken)
    {
        if (context is null) return await IsActiveDriverAsync(reads, id, date, cancellationToken);
        await using var db = Create(context);
        return await IsActiveDriverAsync(db, id, date, cancellationToken);
    }
    private static async Task<bool> IsActiveDriverAsync(FleetOpsDbContext db, Guid id, DateOnly date, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == id &&
            employee.Status == EmployeeStatus.Active && employee.StartDate <= date &&
            (employee.EndDate == null || employee.EndDate >= date), cancellationToken);
        return employee is not null && (JsonSerializer.Deserialize<string[]>(employee.Roles) ?? []).Contains("driver", StringComparer.OrdinalIgnoreCase);
    }
    public async Task<bool> IsUsableTruckAsync(Guid id, DateOnly date, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        if (!await db.Vehicles.AnyAsync(vehicle => vehicle.Id == id && vehicle.Kind == VehicleKind.Truck, cancellationToken)) return false;
        var readiness = await new EfReadinessStore(db).ForVehiclesAsync([id], date, cancellationToken);
        return readiness.GetValueOrDefault(id)?.Usable == true;
    }
    public async Task<IReadOnlyList<Guid>> CycleDriversAsync(Guid vehicleId, DateOnly from, DateOnly? to, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        return await db.Cycles.AsNoTracking().Where(cycle => cycle.VehicleId == vehicleId &&
            cycle.Status != CycleStatus.Cancelled && (to == null || cycle.From <= to) &&
            (cycle.To >= from || cycle.Status == CycleStatus.Overrun)).Select(cycle => cycle.EmployeeId).Distinct().ToListAsync(cancellationToken);
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
