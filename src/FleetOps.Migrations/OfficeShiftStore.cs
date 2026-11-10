using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfOfficeShiftStore(INotifications notifications) : IOfficeShiftStore
{
    public Task<Shift?> LoadAsync(Guid employeeId, Guid shiftId, CommandContext context, CancellationToken cancellationToken) =>
        new EfShiftCommandStore().LoadAsync(employeeId, shiftId, context, cancellationToken);

    public async Task<bool> IsFlaggedAsync(Shift shift, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        return shift.DeviceTimeGap || shift.BlockedAtStart is not null || await db.Alerts.AnyAsync(alert =>
            alert.Status != AlertStatus.Resolved && ((alert.SubjectType == "shift" && alert.SubjectId == shift.Id && (alert.RuleId == "A-16" || alert.RuleId == "A-17")) ||
            (alert.RuleId == "A-17" && alert.SubjectId == shift.EmployeeId && alert.ScopeKey == "shift_conflict:" + shift.Id)), cancellationToken);
    }

    public async Task ValidateReferencesAsync(Shift shift, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context);
        if (!await db.Employees.AnyAsync(employee => employee.Id == shift.EmployeeId && employee.Roles.Contains("\"driver\"") &&
            employee.StartDate <= shift.Date && (employee.EndDate == null || employee.EndDate >= shift.Date), cancellationToken))
            throw new TimesheetValidationException("employeeId");
        if (shift.TruckId is Guid truckId && !await db.Vehicles.AnyAsync(vehicle => vehicle.Id == truckId && vehicle.Kind == VehicleKind.Truck, cancellationToken))
            throw new TimesheetValidationException("truckId");
        if (shift.TrailerId is Guid trailerId && !await db.Vehicles.AnyAsync(vehicle => vehicle.Id == trailerId && vehicle.Kind == VehicleKind.Trailer, cancellationToken))
            throw new TimesheetValidationException("trailerId");
        var activityIds = shift.Activities.Select(activity => activity.Id).ToArray();
        if (await db.ShiftActivities.AnyAsync(activity => activity.ShiftId != shift.Id && activityIds.Contains(activity.Id), cancellationToken))
            throw new TimesheetValidationException("activities");
    }

    public async Task<Guid> SaveAsync(Shift shift, Guid actorId, string reason, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Create(context, actorId, reason);
        var original = await db.Shifts.Include(row => row.Activities).Include(row => row.Overnights).SingleOrDefaultAsync(row => row.Id == shift.Id, cancellationToken);
        var linkedIds = shift.Activities.Select(item => item.Id).Concat(shift.Overnights.Select(item => item.Id))
            .Concat(original?.Activities.Select(item => item.Id) ?? []).Concat(original?.Overnights.Select(item => item.Id) ?? []).Distinct().ToArray();
        if (original is null) db.Shifts.Add(shift);
        else
        {
            db.Entry(original).CurrentValues.SetValues(shift);
            foreach (var activity in original.Activities.ToArray())
            {
                var replacement = shift.Activities.SingleOrDefault(item => item.Id == activity.Id);
                if (replacement is null)
                {
                    AuditRemoval(db, activity.Id, nameof(ShiftActivity), nameof(ShiftActivity.Type), activity.Type.ToString(), actorId, reason);
                    AuditRemoval(db, activity.Id, nameof(ShiftActivity), nameof(ShiftActivity.StartAt), activity.StartAt.ToString("O"), actorId, reason);
                    AuditRemoval(db, activity.Id, nameof(ShiftActivity), nameof(ShiftActivity.EndAt), activity.EndAt?.ToString("O"), actorId, reason);
                    db.ShiftActivities.Remove(activity);
                }
                else db.Entry(activity).CurrentValues.SetValues(replacement);
            }
            foreach (var night in original.Overnights.ToArray())
            {
                var replacement = shift.Overnights.SingleOrDefault(item => item.Id == night.Id);
                if (replacement is null)
                {
                    AuditRemoval(db, night.Id, nameof(Overnight), nameof(Overnight.Night), night.Night.ToString("O"), actorId, reason);
                    AuditRemoval(db, night.Id, nameof(Overnight), nameof(Overnight.CountryCode), night.CountryCode, actorId, reason);
                    AuditRemoval(db, night.Id, nameof(Overnight), nameof(Overnight.Kind), night.Kind.ToString(), actorId, reason);
                    db.Overnights.Remove(night);
                }
                else db.Entry(night).CurrentValues.SetValues(replacement);
            }
            var activityIds = original.Activities.Select(item => item.Id).ToHashSet();
            var nightIds = original.Overnights.Select(item => item.Id).ToHashSet();
            await db.SaveChangesAsync(cancellationToken);
            db.ShiftActivities.AddRange(shift.Activities.Where(item => !activityIds.Contains(item.Id)));
            db.Overnights.AddRange(shift.Overnights.Where(item => !nightIds.Contains(item.Id)));
        }
        await db.SaveChangesAsync(cancellationToken);
        // A root audit entry links additions/removals to this shift even after children are removed.
        var auditId = Guid.CreateVersion7();
        db.AuditEntries.Add(new AuditEntry { Id = auditId, EntityType = nameof(Shift), EntityId = shift.Id, FieldName = "OfficeChange",
            OldValue = string.Join(",", db.AuditEntries.Local.Select(entry => entry.Id)), NewValue = string.Join(",", linkedIds),
            ActorId = actorId.ToString(), Reason = reason, CreatedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return auditId;
    }

    public async Task NotifyAsync(Guid employeeId, Guid shiftId, Guid auditId, string template, CommandContext context, CancellationToken cancellationToken)
    {
        await notifications.AddAsync(employeeId, template, new { shiftId, auditId }, null, context, cancellationToken);
    }

    private static void AuditRemoval(FleetOpsDbContext db, Guid id, string entity, string field, string? before, Guid actor, string reason) =>
        db.AuditEntries.Add(new AuditEntry { Id = Guid.CreateVersion7(), EntityType = entity, EntityId = id, FieldName = field,
            OldValue = before, ActorId = actor.ToString(), Reason = reason, CreatedAt = DateTimeOffset.UtcNow });

    private static FleetOpsDbContext Create(CommandContext context, Guid actorId = default, string reason = "")
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options) { AuditActorId = actorId, AuditReason = reason };
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
}
