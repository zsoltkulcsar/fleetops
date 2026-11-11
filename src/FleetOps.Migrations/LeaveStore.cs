using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfLeaveStore(IConfiguration configuration, TimeProvider time, ICommandHandler<EndOfficeShiftCommand, OfficeShiftResult> endShift) : ILeaveStore, IEmployeeLeftStore
{
    private FleetOpsDbContext Open(CommandContext? context)
    {
        if (context is not null) return AlertEvaluatorDb.CreateDb(context);
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        return new(options.Options);
    }
    public async Task<LeavePreview> PreviewAsync(Guid employeeId, DateOnly endDate, CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        if (!await db.Employees.AnyAsync(employee => employee.Id == employeeId, cancellationToken)) throw new PersonConflictException("not_found");
        var assignments = await db.Assignments.AsNoTracking().Where(assignment => assignment.EmployeeId == employeeId && (assignment.To == null || assignment.To > endDate)).ToArrayAsync(cancellationToken);
        var cycles = await db.Cycles.AsNoTracking().Where(cycle => cycle.EmployeeId == employeeId && cycle.To > endDate).ToArrayAsync(cancellationToken);
        var documentIds = db.Documents.Where(document => document.OwnerType == FleetOps.Modules.Documents.DocumentOwnerType.Employee && document.OwnerId == employeeId).Select(document => document.Id);
        var shiftIds = db.Shifts.Where(shift => shift.EmployeeId == employeeId).Select(shift => shift.Id);
        var cycleIds = db.Cycles.Where(cycle => cycle.EmployeeId == employeeId).Select(cycle => cycle.Id);
        var alerts = await db.Alerts.Where(alert => alert.Status != AlertStatus.Resolved &&
            ((alert.SubjectType == "employee" && alert.SubjectId == employeeId) || (alert.SubjectType == "document" && documentIds.Contains(alert.SubjectId)) || (alert.SubjectType == "shift" && shiftIds.Contains(alert.SubjectId)) || (alert.SubjectType == "cycle" && cycleIds.Contains(alert.SubjectId))))
            .Select(alert => alert.Id).ToArrayAsync(cancellationToken);
        var running = await db.Shifts.Where(shift => shift.EmployeeId == employeeId && shift.Status == ShiftStatus.Running).Select(shift => new { shift.Id, shift.Version }).SingleOrDefaultAsync(cancellationToken);
        return new(employeeId, endDate,
            await db.OfficeSessions.Where(session => session.EmployeeId == employeeId && session.RevokedAt == null).Select(session => session.Id).ToArrayAsync(cancellationToken),
            assignments.Where(assignment => assignment.From <= endDate).Select(assignment => assignment.Id).ToArray(),
            assignments.Where(assignment => assignment.From > endDate).Select(assignment => assignment.Id).ToArray(),
            cycles.Where(cycle => cycle.From <= endDate).Select(cycle => cycle.Id).ToArray(),
            cycles.Where(cycle => cycle.From > endDate).Select(cycle => cycle.Id).ToArray(), alerts, running?.Id, running?.Version,
            await db.Shifts.Where(shift => shift.EmployeeId == employeeId && shift.Date <= endDate && shift.Status != ShiftStatus.Approved && shift.Status != ShiftStatus.Exported).Select(shift => shift.Id).ToArrayAsync(cancellationToken));
    }
    public async Task EndShiftAsync(EmployeeLeft notification, LeavePreview preview, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await endShift.HandleAsync(new(preview.RunningShiftId!.Value, notification.EmployeeId, preview.RunningShiftVersion!.Value, notification.RunningShiftReason!, notification.RunningShiftEnd!.Value, null), actor, context, cancellationToken);
    }
    public async Task ApplyAsync(EmployeeLeft notification, LeavePreview preview, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var now = time.GetUtcNow();
        await db.OfficeSessions.Where(session => session.EmployeeId == notification.EmployeeId && session.RevokedAt == null).ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAt, now), cancellationToken);
        await db.Assignments.Where(assignment => preview.AssignmentsToRemove.Contains(assignment.Id)).ExecuteDeleteAsync(cancellationToken);
        await db.Assignments.Where(assignment => preview.AssignmentsToEnd.Contains(assignment.Id)).ExecuteUpdateAsync(update => update.SetProperty(assignment => assignment.To, notification.EndDate), cancellationToken);
        await db.Cycles.Where(cycle => preview.CyclesToRemove.Contains(cycle.Id)).ExecuteDeleteAsync(cancellationToken);
        await db.Cycles.Where(cycle => preview.CyclesToEnd.Contains(cycle.Id)).ExecuteUpdateAsync(update => update.SetProperty(cycle => cycle.To, notification.EndDate).SetProperty(cycle => cycle.Version, cycle => cycle.Version + 1), cancellationToken);
        var resolvedAlerts = await db.Alerts.AsNoTracking().Where(alert => preview.AlertsToResolve.Contains(alert.Id)).ToArrayAsync(cancellationToken);
        foreach (var alert in resolvedAlerts)
        {
            db.AuditEntries.Add(new FleetOps.Modules.Audit.AuditEntry
            {
                Id = Guid.CreateVersion7(),
                EntityType = "alert",
                EntityId = alert.Id,
                FieldName = "status",
                OldValue = alert.Status.ToString(),
                NewValue = "Resolved",
                ActorId = "system",
                Reason = "Person left",
                CreatedAt = now
            });
        }
        await db.SaveChangesAsync(cancellationToken);
        await db.Alerts.Where(alert => preview.AlertsToResolve.Contains(alert.Id)).ExecuteUpdateAsync(update => update.SetProperty(alert => alert.Status, AlertStatus.Resolved).SetProperty(alert => alert.ResolvedAt, now).SetProperty(alert => alert.ResolvedBy, (Guid?)null).SetProperty(alert => alert.Version, alert => alert.Version + 1), cancellationToken);
    }
}
