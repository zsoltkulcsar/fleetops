using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Shifts;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class ShiftEvents(IChangedSubjects changed, INotifications notifications, CycleCompletion completion, TimeProvider clock) : IShiftEvents
{
    public async Task ChangedAsync(Guid employeeId, Guid shiftId, string? eventCode, CommandContext context, CancellationToken cancellationToken)
    {
        changed.Add("employee", employeeId);
        changed.Add("shift", shiftId);
        if (eventCode is not ("sync_conflict" or "cycle_finished")) return;
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        await using var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        if (eventCode == "cycle_finished")
        {
            await EfCycleStore.LockScheduleAsync(context, cancellationToken);
            var shiftDate = await db.Shifts.Where(shift => shift.Id == shiftId && shift.EmployeeId == employeeId).Select(shift => (DateOnly?)shift.Date).SingleOrDefaultAsync(cancellationToken);
            if (shiftDate is not null) await completion.FinishAsync(employeeId, shiftDate.Value, clock.GetUtcNow(), context, cancellationToken);
            return;
        }
        await notifications.AddAsync(employeeId, "sync_conflict", new { shiftId }, null, context, cancellationToken);
        var office = await db.Employees.Where(employee => employee.Id != employeeId && employee.Status == FleetOps.People.EmployeeStatus.Active &&
            (employee.Roles.Contains("\"hr\"") || employee.Roles.Contains("\"dispatcher\"")))
            .Select(employee => employee.Id).ToListAsync(cancellationToken);
        foreach (var recipient in office)
            await notifications.AddAsync(recipient, "sync_conflict", new { shiftId }, null, context, cancellationToken);
    }
}
