using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Schedule;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfScheduleEditor(IChangedSubjects changedSubjects) : IScheduleEditor
{
    public async Task<CycleMutation> AddAsync(AddManualCycleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await EfCycleStore.LockScheduleAsync(context, cancellationToken);
        await EfCycleStore.LockEmployeeAsync(command.EmployeeId, context, cancellationToken);
        await using var db = CreateDb(context, actor.EmployeeId, command.Reason);
        await ValidateTargets(db, command.EmployeeId, command.VehicleId, cancellationToken);
        var cycle = new Cycle { EmployeeId = command.EmployeeId, VehicleId = command.VehicleId, From = command.From, To = command.To, Source = CycleSource.Manual };
        db.Cycles.Add(cycle);
        await db.SaveChangesAsync(cancellationToken);
        changedSubjects.Add("vehicle", cycle.VehicleId);
        return new(await Detail(db, cycle, cancellationToken), null);
    }

    public async Task<CycleMutation> EditAsync(EditCycleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await EfCycleStore.LockScheduleAsync(context, cancellationToken);
        await using var db = CreateDb(context, actor.EmployeeId, command.Reason);
        var cycle = await db.Cycles.SingleOrDefaultAsync(cycle => cycle.Id == command.Id, cancellationToken) ?? throw new ScheduleConflictException("cycle_not_found");
        foreach (var employeeId in new[] { cycle.EmployeeId, command.EmployeeId }.Distinct().Order())
            await EfCycleStore.LockEmployeeAsync(employeeId, context, cancellationToken);
        if (cycle.Version != command.Version) throw new ScheduleConflictException("version_conflict");
        if (cycle.Status is CycleStatus.Done or CycleStatus.Cancelled) throw new ScheduleConflictException("state_conflict");
        // A generated slot belongs to its original employee, even after a date/truck edit.
        if (cycle.PatternSlot is not null && cycle.EmployeeId != command.EmployeeId) throw new ScheduleValidationException(["employeeId"]);
        await ValidateTargets(db, command.EmployeeId, command.VehicleId, cancellationToken);
        var previousEmployeeId = cycle.EmployeeId;
        changedSubjects.Add("vehicle", cycle.VehicleId);
        cycle.EmployeeId = command.EmployeeId;
        cycle.VehicleId = command.VehicleId;
        cycle.From = command.From;
        cycle.To = command.To;
        cycle.Source = CycleSource.Manual;
        cycle.Version++;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ScheduleConflictException("version_conflict"); }
        changedSubjects.Add("vehicle", cycle.VehicleId);
        return new(await Detail(db, cycle, cancellationToken), previousEmployeeId);
    }

    private static async Task ValidateTargets(FleetOpsDbContext db, Guid employeeId, Guid vehicleId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == employeeId, cancellationToken);
        if (employee is null || employee.Status != EmployeeStatus.Active || !(JsonSerializer.Deserialize<string[]>(employee.Roles)?.Contains("driver") ?? false)) throw new ScheduleValidationException(["employeeId"]);
        if (!await db.Vehicles.AnyAsync(vehicle => vehicle.Id == vehicleId && vehicle.Kind == VehicleKind.Truck && vehicle.Status == VehicleStatus.Active, cancellationToken)) throw new ScheduleValidationException(["vehicleId"]);
    }

    private static async Task<CycleDetail> Detail(FleetOpsDbContext db, Cycle cycle, CancellationToken cancellationToken)
    {
        var name = await db.Employees.Where(employee => employee.Id == cycle.EmployeeId).Select(employee => employee.FirstName + " " + employee.LastName).SingleAsync(cancellationToken);
        var plate = await db.Vehicles.Where(vehicle => vehicle.Id == cycle.VehicleId).Select(vehicle => vehicle.Plate).SingleAsync(cancellationToken);
        return new(cycle.Id, cycle.EmployeeId, name, cycle.VehicleId, plate, cycle.From, cycle.To, cycle.Status, cycle.Source, cycle.PatternSlot, cycle.Version);
    }

    private static FleetOpsDbContext CreateDb(CommandContext context, Guid actorId, string reason)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options) { AuditActorId = actorId, AuditReason = reason };
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
}
