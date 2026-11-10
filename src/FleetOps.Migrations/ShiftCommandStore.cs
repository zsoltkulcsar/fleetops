using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfShiftCommandStore : IShiftCommandStore
{
    public async Task LockEmployeeAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, employeeId);
        var key = $"driver-shifts:{employeeId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
    }

    public async Task<CommandReceipt?> ReceiptAsync(Guid employeeId, Guid commandId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, employeeId);
        return await db.CommandReceipts.AsNoTracking().SingleOrDefaultAsync(receipt => receipt.EmployeeId == employeeId && receipt.CommandId == commandId, cancellationToken);
    }

    public async Task<bool> HasFailedDependencyAsync(Guid employeeId, Guid aggregateId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, employeeId);
        return await db.CommandReceipts.AnyAsync(receipt => receipt.EmployeeId == employeeId && receipt.AggregateId == aggregateId && receipt.Outcome != "applied", cancellationToken);
    }

    public async Task<Shift?> LoadAsync(Guid employeeId, Guid shiftId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, employeeId);
        var key = $"driver-shift:{shiftId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", cancellationToken);
        var shift = await db.Shifts.AsNoTracking().Include(shift => shift.Activities).Include(shift => shift.Overnights)
            .SingleOrDefaultAsync(shift => shift.Id == shiftId, cancellationToken);
        if (shift is not null && shift.EmployeeId != employeeId) throw new ShiftRuleException("shift_not_found");
        return shift;
    }

    public async Task<bool> HasRunningShiftAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, employeeId);
        return await db.Shifts.AnyAsync(shift => shift.EmployeeId == employeeId && shift.Status == ShiftStatus.Running, cancellationToken);
    }

    public async Task SaveAsync(Shift? shift, CommandReceipt receipt, ConflictRecord? conflict, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, receipt.EmployeeId);
        if (shift is not null)
        {
            if (!await db.Shifts.AnyAsync(row => row.Id == shift.Id, cancellationToken)) db.Shifts.Add(shift);
            else
            {
                var original = await db.Shifts.Include(row => row.Activities).Include(row => row.Overnights).SingleAsync(row => row.Id == shift.Id, cancellationToken);
                if (original.EmployeeId != receipt.EmployeeId || shift.EmployeeId != receipt.EmployeeId) throw new ShiftRuleException("shift_not_found");
                var activityIds = original.Activities.Select(activity => activity.Id).ToArray();
                var overnightIds = original.Overnights.Select(overnight => overnight.Id).ToArray();
                db.Entry(original).CurrentValues.SetValues(shift);
                foreach (var activity in shift.Activities.Where(activity => activityIds.Contains(activity.Id)))
                    db.Entry(original.Activities.Single(row => row.Id == activity.Id)).CurrentValues.SetValues(activity);
                foreach (var overnight in shift.Overnights.Where(overnight => overnightIds.Contains(overnight.Id)))
                    db.Entry(original.Overnights.Single(row => row.Id == overnight.Id)).CurrentValues.SetValues(overnight);
                // Close the previous open activity before inserting its successor;
                // the partial unique index also protects non-API writers.
                await db.SaveChangesAsync(cancellationToken);
                foreach (var activity in shift.Activities.Where(activity => !activityIds.Contains(activity.Id))) db.ShiftActivities.Add(activity);
                foreach (var overnight in shift.Overnights.Where(overnight => !overnightIds.Contains(overnight.Id))) db.Overnights.Add(overnight);
            }
        }
        db.CommandReceipts.Add(receipt);
        if (conflict is not null) db.ShiftConflicts.Add(conflict);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static FleetOpsDbContext Create(CommandContext context, Guid actorId)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options) { AuditActorId = actorId, AuditReason = "driver_command" };
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
}
