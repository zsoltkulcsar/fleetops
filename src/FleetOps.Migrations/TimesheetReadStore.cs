using FleetOps.Kernel;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfTimesheetReadStore(FleetOpsDbContext db, IConfiguration configuration, ICompanyTimeService companyTime) : ITimesheetReadStore
{
    public string CompanyTimeZone => configuration["Company:Timezone"] ?? "Europe/Berlin";

    public async Task<IReadOnlyList<WeekDriverFacts>> WeekAsync(DateOnly week, string? search, CancellationToken cancellationToken)
    {
        var end = week.AddDays(6);
        var employees = db.Employees.AsNoTracking().Where(employee => employee.Roles.Contains("\"driver\"") && employee.StartDate <= end &&
            (((employee.Status == EmployeeStatus.Active || employee.Status == EmployeeStatus.Suspended) && (employee.EndDate == null || employee.EndDate >= week)) || employee.EndDate >= week));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLowerInvariant();
            employees = employees.Where(employee => (employee.FirstName + " " + employee.LastName).ToLower().Contains(term) ||
                (employee.PersonnelNumber != null && employee.PersonnelNumber.ToLower().Contains(term)));
        }
        var drivers = await employees.OrderBy(employee => employee.LastName).ThenBy(employee => employee.FirstName).ThenBy(employee => employee.Id).ToListAsync(cancellationToken);
        var ids = drivers.Select(employee => employee.Id).ToArray();
        var shifts = await db.Shifts.AsNoTracking().Include(shift => shift.Activities).Include(shift => shift.Overnights)
            .Where(shift => ids.Contains(shift.EmployeeId) && shift.Date >= week && shift.Date <= end).ToListAsync(cancellationToken);
        var absences = await db.Absences.AsNoTracking().Where(absence => ids.Contains(absence.EmployeeId) && absence.Status == AbsenceStatus.Approved && absence.From <= end && absence.To >= week).ToListAsync(cancellationToken);
        var alerts = await db.Alerts.AsNoTracking().Where(alert => alert.Status != AlertStatus.Resolved && (alert.RuleId == "A-16" || alert.RuleId == "A-17")).ToListAsync(cancellationToken);
        var days = Enumerable.Range(0, 7).Select(offset => week.AddDays(offset)).ToArray();
        var cycleStore = new EfIsInCycle(db);
        var result = new List<WeekDriverFacts>();
        foreach (var employee in drivers)
        {
            var ownShifts = shifts.Where(shift => shift.EmployeeId == employee.Id).ToArray();
            var flagged = ownShifts.Where(shift => alerts.Any(alert => (alert.SubjectType == "shift" && alert.SubjectId == shift.Id) ||
                (alert.SubjectId == employee.Id && alert.ScopeKey == "shift_conflict:" + shift.Id))).Select(shift => shift.Id).ToHashSet();
            result.Add(new(employee.Id, employee.FirstName + " " + employee.LastName, employee.PersonnelNumber, ownShifts,
                await cycleStore.ForEmployeeDatesAsync(employee.Id, days.Where(date => date >= employee.StartDate && (employee.EndDate == null || date <= employee.EndDate)).ToArray(), cancellationToken),
                absences.Where(absence => absence.EmployeeId == employee.Id).Select(absence => new WeekAbsence(absence.From, absence.To, absence.Type.ToString())).ToArray(), flagged));
        }
        return result;
    }

    public Task<int> AwaitingPeriodAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        db.Shifts.CountAsync(shift => shift.Status == ShiftStatus.Submitted && shift.Date >= from && shift.Date <= to, cancellationToken);

    public async Task<ShiftDetail?> DetailAsync(Guid shiftId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var shift = await db.Shifts.AsNoTracking().Include(row => row.Activities).Include(row => row.Overnights).SingleOrDefaultAsync(row => row.Id == shiftId, cancellationToken);
        if (shift is null) return null;
        var driver = await db.Employees.Where(employee => employee.Id == shift.EmployeeId).Select(employee => employee.FirstName + " " + employee.LastName).SingleAsync(cancellationToken);
        var plate = await db.Vehicles.Where(vehicle => vehicle.Id == shift.TruckId).Select(vehicle => vehicle.Plate).SingleOrDefaultAsync(cancellationToken);
        var conflicts = await db.ShiftConflicts.AsNoTracking().Where(conflict => conflict.ShiftId == shiftId).OrderByDescending(conflict => conflict.CreatedAt)
            .Select(conflict => new ShiftConflictView(conflict.Id, conflict.CommandId, conflict.BaseVersion, conflict.OfficeVersion, conflict.DriverPayload, conflict.CreatedAt)).ToListAsync(cancellationToken);
        var flags = new List<string>();
        if (shift.DeviceTimeGap) flags.Add("Phone clock differed from server time by more than 5 min");
        if (shift.BlockedAtStart is not null) flags.Add("Started while blocked: " + shift.BlockedAtStart);
        var rules = await db.Alerts.Where(alert => alert.Status != AlertStatus.Resolved &&
            ((alert.SubjectType == "shift" && alert.SubjectId == shiftId && (alert.RuleId == "A-16" || alert.RuleId == "A-17")) ||
            (alert.RuleId == "A-17" && alert.SubjectId == shift.EmployeeId && alert.ScopeKey == "shift_conflict:" + shiftId)))
            .Select(alert => alert.RuleId).Distinct().ToListAsync(cancellationToken);
        flags.AddRange(rules);
        var view = ShiftViews.From(shift);
        return new(shift.EmployeeId, driver, plate, new(shift.Id, shift.Date, shift.StartAt, shift.EndAt, shift.TruckId, shift.TrailerId,
            shift.Status, shift.Source, shift.Version, shift.DepartureCheckOk, shift.OdometerStartKm, shift.OdometerEndKm, shift.SubmittedAt,
            shift.ApprovedBy, shift.ApprovedAt, shift.ApprovalNote, shift.RejectReason, view.Activities, view.Overnights,
            ShiftRules.Hours(shift, ActivityType.Driving, now), ShiftRules.Hours(shift, ActivityType.OtherWork, now),
            ShiftRules.Hours(shift, ActivityType.Break, now), ShiftRules.Hours(shift, ActivityType.Availability, now)), flags, conflicts,
            await HistoryAsync(db, shift, companyTime, cancellationToken));
    }

    public static async Task<IReadOnlyList<ShiftHistory>> HistoryAsync(FleetOpsDbContext db, Shift shift, ICompanyTimeService companyTime, CancellationToken cancellationToken, bool officeOnly = false)
    {
        var root = await db.AuditEntries.AsNoTracking().Where(entry => entry.EntityType == nameof(Shift) && entry.EntityId == shift.Id).ToListAsync(cancellationToken);
        var linked = root.Where(entry => entry.FieldName == "OfficeChange" && entry.NewValue != null)
            .SelectMany(entry => entry.NewValue!.Split(',')).Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty);
        var ids = shift.Activities.Select(activity => activity.Id).Concat(shift.Overnights.Select(night => night.Id)).Concat(linked).Distinct().ToArray();
        var childEntries = await db.AuditEntries.AsNoTracking().Where(entry => (entry.EntityType == nameof(ShiftActivity) || entry.EntityType == nameof(Overnight)) && ids.Contains(entry.EntityId)).ToListAsync(cancellationToken);
        var officeAuditIds = root.Where(entry => entry.FieldName == "OfficeChange" && entry.OldValue != null)
            .SelectMany(entry => entry.OldValue!.Split(',')).Select(value => Guid.TryParse(value, out var id) ? id : Guid.Empty).ToHashSet();
        var entries = root.Concat(childEntries).Where(entry => entry.FieldName != "OfficeChange" && (!officeOnly || officeAuditIds.Contains(entry.Id)))
            .OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Id).ToArray();
        var actorIds = entries.Select(entry => Guid.TryParse(entry.ActorId, out var id) ? id : Guid.Empty).Distinct().ToArray();
        var editors = await db.Employees.Where(employee => actorIds.Contains(employee.Id)).ToDictionaryAsync(employee => employee.Id, employee => employee.FirstName + " " + employee.LastName, cancellationToken);
        return entries.Select(entry => new ShiftHistory(entry.Id, entry.EntityType, entry.EntityId, entry.FieldName,
            CompanyValue(entry.FieldName, entry.OldValue, companyTime), CompanyValue(entry.FieldName, entry.NewValue, companyTime),
            Guid.TryParse(entry.ActorId, out var id) ? editors.GetValueOrDefault(id, "Office") : "Office", entry.Reason, companyTime.ToCompanyTime(entry.CreatedAt))).ToArray();
    }

    private static string? CompanyValue(string field, string? value, ICompanyTimeService companyTime) =>
        field.EndsWith("At", StringComparison.Ordinal) && DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out var instant) ? companyTime.ToCompanyTime(instant).ToString("O") : value;
}
