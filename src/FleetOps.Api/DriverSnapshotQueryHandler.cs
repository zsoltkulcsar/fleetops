using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Views;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class DriverSnapshotQueryHandler(FleetOpsDbContext db, ICompanyTimeService companyTime, TimeProvider time, FleetOps.Modules.Absences.IAbsenceStore absences, IScheduleRequestStore scheduleRequests) : IQueryHandler<GetDriverSnapshotQuery, DriverSnapshot>
{
    public async Task<DriverSnapshot> HandleAsync(GetDriverSnapshotQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var generatedAt = time.GetUtcNow();
        var today = companyTime.ToCompanyDate(generatedAt);
        var horizon = today.AddDays(84);
        var cycles = await db.Cycles.AsNoTracking().Where(cycle => cycle.EmployeeId == actor.EmployeeId && (cycle.To >= today.AddDays(-28) || cycle.Status == CycleStatus.Overrun) && cycle.From < horizon)
            .OrderBy(cycle => cycle.From).Select(cycle => new DriverCycle(cycle.Id, cycle.VehicleId, db.Vehicles.Where(vehicle => vehicle.Id == cycle.VehicleId).Select(vehicle => vehicle.Plate).FirstOrDefault() ?? "", cycle.From, cycle.To, cycle.Status, cycle.Source, cycle.Version)).ToListAsync(cancellationToken);
        var documents = await db.Documents.AsNoTracking().Where(document => document.OwnerType == DocumentOwnerType.Employee && document.OwnerId == actor.EmployeeId && document.Status != DocumentStatus.Replaced)
            .OrderBy(document => document.Type).ThenByDescending(document => document.CreatedAt).Select(document => new DriverDocument(document.Id, document.Type, document.Status, document.ExpiresAt, document.RejectReason)).ToListAsync(cancellationToken);
        var readiness = await new EfReadinessStore(db).ForDriversAsync([actor.EmployeeId], today, cancellationToken);
        var own = readiness.GetValueOrDefault(actor.EmployeeId) ?? new DriverReadiness(false, []);
        var defects = await db.DefectReports.AsNoTracking().Where(defect => defect.ReportedBy == actor.EmployeeId).OrderByDescending(defect => defect.ReportedAt).ToListAsync(cancellationToken);
        var absenceFacts = await absences.FactsAsync(actor.EmployeeId, null, cancellationToken);
        return new(actor.EmployeeId, generatedAt, cycles, documents, own.CanDrive, own.Causes)
        {
            VacationBalance = absenceFacts is null ? null : FleetOps.Modules.Absences.AbsenceRules.Balance(absenceFacts, today.Year),
            Absences = absenceFacts?.Absences.Select(absence => FleetOps.Modules.Absences.AbsenceRules.View(absence) with { WorkingDays = Enumerable.Range(absence.From.Year, absence.To.Year - absence.From.Year + 1).Sum(year => FleetOps.Modules.Absences.AbsenceRules.Days(absence, year, absenceFacts.Holidays)) }).ToArray() ?? [],
            ScheduleRequests = await scheduleRequests.ListAsync(actor.EmployeeId, cancellationToken),
            EmployeeStatus = (await db.Employees.Where(employee => employee.Id == actor.EmployeeId).Select(employee => employee.Status).SingleAsync(cancellationToken)).ToString().ToLowerInvariant(),
            Swaps = await db.Assignments.AsNoTracking().Where(assignment => assignment.EmployeeId == actor.EmployeeId &&
                assignment.From < horizon && (assignment.To == null || assignment.To >= today.AddDays(-28)))
                .OrderBy(assignment => assignment.From).Select(assignment => new DriverSwap(assignment.Id, assignment.VehicleId,
                    db.Vehicles.Where(vehicle => vehicle.Id == assignment.VehicleId).Select(vehicle => vehicle.Plate).First(),
                    assignment.From, assignment.To)).ToListAsync(cancellationToken),
            Defects = defects.Select(defect => new DriverOwnDefect(defect.Id, defect.VehicleId, System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(defect.Category.ToString()), System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(defect.Severity.ToString()), System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(defect.Status.ToString()), defect.ReportedAt)).ToArray()
        };
    }
}

