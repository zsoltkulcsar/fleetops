using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class VehicleRetiredScheduleEffects(INotifications notifications, IChangedSubjects changedSubjects, ICompanyTimeService companyTime, TimeProvider time) : IVehicleRetiredEffects
{
    public async Task HandleAsync(VehicleRetired notification, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await EfCycleStore.LockScheduleAsync(context, cancellationToken);
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        await using var db = new FleetOpsDbContext(options.Options) { AuditActorId = actor.EmployeeId, AuditReason = "vehicle_retired" };
        db.Database.UseTransaction(context.DbTransaction);
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var cycles = await db.Cycles.Where(cycle => cycle.VehicleId == notification.VehicleId && cycle.To >= today && cycle.Status != CycleStatus.Done && cycle.Status != CycleStatus.Cancelled).ToListAsync(cancellationToken);
        foreach (var cycle in cycles)
        {
            cycle.Status = CycleStatus.Cancelled;
            cycle.FinishedAt = time.GetUtcNow();
            cycle.Version++;
            changedSubjects.Add("cycle", cycle.Id);
        }
        await db.SaveChangesAsync(cancellationToken);
        foreach (var employeeId in cycles.Select(cycle => cycle.EmployeeId).Distinct())
            await notifications.AddAsync(employeeId, "schedule_changed", new { vehicleId = notification.VehicleId, reason = "vehicle_retired" }, null, context, cancellationToken);
    }
}
