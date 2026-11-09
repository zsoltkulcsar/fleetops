using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Schedule;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetOps.Migrations;

public sealed class EfCycleStore(FleetOps.Kernel.IChangedSubjects? changedSubjects = null) : ICycleStore
{
    public static async Task LockScheduleAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await using var command = context.DbConnection.CreateCommand();
        command.Transaction = context.DbTransaction;
        command.CommandText = "select pg_advisory_xact_lock(320032)";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public static async Task LockEmployeeAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var command = context.DbConnection.CreateCommand();
        command.Transaction = context.DbTransaction;
        command.CommandText = "select pg_advisory_xact_lock(hashtextextended(@employee, 32))";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "employee";
        parameter.Value = employeeId.ToString();
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SavePatternAsync(SaveCyclePatternCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var employeeId = command.SubjectId!.Value;
        await LockScheduleAsync(context, cancellationToken);
        await LockEmployeeAsync(employeeId, context, cancellationToken);
        await using var db = CreateDb(context, actor.EmployeeId);
        var employee = await db.Employees.SingleOrDefaultAsync(employee => employee.Id == employeeId, cancellationToken);
        if (employee is null || employee.Status != EmployeeStatus.Active || !(JsonSerializer.Deserialize<string[]>(employee.Roles)?.Contains("driver") ?? false))
            throw new ScheduleConflictException("driver_not_available");
        if (employee.CyclePatternVersion != command.Version) throw new ScheduleConflictException("version_conflict");
        if (!await db.Vehicles.AnyAsync(vehicle => vehicle.Id == command.Pattern.VehicleId && vehicle.Kind == VehicleKind.Truck && vehicle.Status == VehicleStatus.Active, cancellationToken))
            throw new ScheduleValidationException(["vehicleId"]);
        var oldPattern = employee.CyclePattern;
        employee.CyclePattern = command.Pattern;
        employee.CyclePatternVersion++;
        db.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.CreateVersion7(), EntityType = "Employee", EntityId = employeeId,
            FieldName = "CyclePattern", OldValue = JsonSerializer.Serialize(oldPattern), NewValue = JsonSerializer.Serialize(command.Pattern),
            ActorId = actor.EmployeeId.ToString(), Reason = "cycle_pattern_changed", CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Cycle>> LoadCyclesAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, Guid.Empty);
        return await db.Cycles.AsNoTracking().Where(cycle => cycle.EmployeeId == employeeId).ToListAsync(cancellationToken);
    }

    public async Task AddCyclesAsync(IReadOnlyList<Cycle> cycles, CommandContext context, Guid actorId, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actorId);
        db.Cycles.AddRange(cycles);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var cycle in cycles) changedSubjects?.Add("cycle", cycle.Id);
    }

    public async Task RemoveFutureGeneratedAsync(Guid employeeId, DateOnly today, CommandContext context, Guid actorId, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actorId);
        var cycles = await db.Cycles.Where(cycle => cycle.EmployeeId == employeeId && cycle.Source == CycleSource.Generated && cycle.Status == CycleStatus.Planned && cycle.From > today).ToListAsync(cancellationToken);
        foreach (var cycle in cycles)
        {
            changedSubjects?.Add("cycle", cycle.Id);
            changedSubjects?.Add("vehicle", cycle.VehicleId);
            db.AuditEntries.Add(new AuditEntry
            {
                Id = Guid.CreateVersion7(), EntityType = "Cycle", EntityId = cycle.Id, FieldName = "Regenerated",
                OldValue = cycle.Version.ToString(), ActorId = actorId.ToString(), Reason = "cycle_pattern_changed", CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        db.Cycles.RemoveRange(cycles);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static FleetOpsDbContext CreateDb(CommandContext context, Guid actorId)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options) { AuditActorId = actorId, AuditReason = "cycle_generation" };
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
}

