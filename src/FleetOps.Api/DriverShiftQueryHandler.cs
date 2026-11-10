using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FleetOps.Api;

public sealed class DriverShiftQueryHandler(FleetOpsDbContext db, ICompanyTimeService companyTime, TimeProvider clock, IConfiguration configuration)
    : IQueryHandler<GetDriverShiftsQuery, ShiftSnapshot>
{
    public async Task<ShiftSnapshot> HandleAsync(GetDriverShiftsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var today = companyTime.ToCompanyDate(now);
        var from = today.AddDays(-90);
        var shifts = await db.Shifts.AsNoTracking().Include(shift => shift.Activities).Include(shift => shift.Overnights)
            .Where(shift => shift.EmployeeId == actor.EmployeeId && (shift.Date >= from || shift.Status == ShiftStatus.Running))
            .OrderBy(shift => shift.StartAt).ToListAsync(cancellationToken);
        var applied = await db.CommandReceipts.AsNoTracking().Where(receipt => receipt.EmployeeId == actor.EmployeeId && receipt.Outcome == "applied" && receipt.ServerReceivedAt >= now.AddDays(-120))
            .Select(receipt => receipt.CommandId).ToListAsync(cancellationToken);
        var vehicles = await db.Vehicles.AsNoTracking().Where(vehicle => vehicle.Status != FleetOps.Modules.Fleet.VehicleStatus.Retired).OrderBy(vehicle => vehicle.Plate).ToListAsync(cancellationToken);
        var readiness = await new EfReadinessStore(db).ForVehiclesAsync(vehicles.Select(vehicle => vehicle.Id).ToArray(), today, cancellationToken);
        var licences = await db.Documents.AsNoTracking().Where(document => document.OwnerId == actor.EmployeeId && document.OwnerType == FleetOps.Modules.Documents.DocumentOwnerType.Employee &&
            document.Type == FleetOps.Modules.Documents.DocumentType.LicenceC_CE && document.Status == FleetOps.Modules.Documents.DocumentStatus.Verified && document.ExpiresAt >= today)
            .Select(document => document.Categories).ToListAsync(cancellationToken);
        var classes = licences.SelectMany(categories => categories).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = vehicles.Select(vehicle =>
        {
            var ready = readiness.GetValueOrDefault(vehicle.Id);
            return new ShiftTruckChoice(vehicle.Id, vehicle.Plate, vehicle.Kind.ToString().ToLowerInvariant(), ready?.Usable == true,
                ready?.Usable == true ? null : "Unavailable — ask dispatch", vehicle.RequiredLicenceClass is not null && !classes.Contains(vehicle.RequiredLicenceClass));
        }).ToArray();
        var conflicts = await db.ShiftConflicts.AsNoTracking().Where(conflict => conflict.EmployeeId == actor.EmployeeId).OrderByDescending(conflict => conflict.CreatedAt).Take(100).ToListAsync(cancellationToken);
        var conflictViews = new List<DriverShiftConflict>();
        foreach (var conflict in conflicts)
        {
            var receipt = await db.CommandReceipts.AsNoTracking().SingleAsync(receipt => receipt.EmployeeId == actor.EmployeeId && receipt.CommandId == conflict.CommandId, cancellationToken);
            var ownActor = actor.EmployeeId.ToString();
            var edits = await db.AuditEntries.AsNoTracking().Where(entry => entry.EntityId == conflict.ShiftId && entry.ActorId != ownActor)
                .OrderByDescending(entry => entry.CreatedAt).Take(10).Select(entry => new { entry.ActorId, entry.FieldName }).ToListAsync(cancellationToken);
            var officeId = edits.FirstOrDefault()?.ActorId;
            var name = Guid.TryParse(officeId, out var editorId) ? await db.Employees.Where(employee => employee.Id == editorId).Select(employee => employee.FirstName + " " + employee.LastName).FirstOrDefaultAsync(cancellationToken) : null;
            using var payload = JsonDocument.Parse(conflict.DriverPayload);
            DateTimeOffset? proposedStart = payload.RootElement.TryGetProperty("startAt", out var start) && start.ValueKind == JsonValueKind.String && start.TryGetDateTimeOffset(out var startAt) ? startAt : receipt.Type == "shift.switch" ? receipt.DeviceTime : null;
            DateTimeOffset? proposedEnd = payload.RootElement.TryGetProperty("endAt", out var end) && end.ValueKind == JsonValueKind.String && end.TryGetDateTimeOffset(out var endAt) ? endAt : receipt.Type == "shift.end" ? receipt.DeviceTime : null;
            conflictViews.Add(new(conflict.CommandId, conflict.ShiftId, receipt.Type, name ?? "Office", edits.Select(edit => edit.FieldName).Distinct().ToArray(), proposedStart, proposedEnd, conflict.OfficeVersion));
        }
        var assignments = await db.Assignments.AsNoTracking().Where(assignment => assignment.EmployeeId == actor.EmployeeId && assignment.From <= today.AddDays(84) &&
            (assignment.To == null || assignment.To >= from)).OrderByDescending(assignment => assignment.From).ThenByDescending(assignment => assignment.Id)
            .Select(assignment => new DriverTruckAssignment(assignment.VehicleId, assignment.From, assignment.To)).ToListAsync(cancellationToken);
        return new(now, configuration["Company:Timezone"] ?? "Europe/Berlin", shifts.Select(ShiftViews.From).ToArray(), applied, choices,
            await EfShiftStartFacts.ResolveTruckForShift(db, actor.EmployeeId, today, cancellationToken), conflictViews, assignments);
    }
}
