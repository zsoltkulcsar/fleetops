using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;

namespace FleetOps.Modules.Shifts;

public sealed class TimesheetQueries(ITimesheetReadStore store, ICompanyTimeService companyTime, ISettings settings, TimeProvider clock) :
    IQueryHandler<GetWeekGridQuery, WeekGrid>, IQueryHandler<GetShiftDetailQuery, ShiftDetail>
{
    public async Task<WeekGrid> HandleAsync(GetWeekGridQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.Week == default || query.Week.DayOfWeek != DayOfWeek.Monday || query.Week > DateOnly.MaxValue.AddDays(-7))
            throw new TimesheetValidationException("week");
        if (query.Page < 1 || query.PageSize is < 1 or > 200) throw new TimesheetValidationException("page", "pageSize");
        if (query.Filter is not ("all" or "awaiting" or "missing" or "warnings")) throw new TimesheetValidationException("filter");
        if (query.Search?.Length > 200) throw new TimesheetValidationException("search");
        var now = clock.GetUtcNow();
        var today = companyTime.ToCompanyDate(now);
        var facts = await store.WeekAsync(query.Week, query.Search?.Trim(), cancellationToken);
        var rows = facts.Select(driver => Row(driver, query.Week, today, now)).ToArray();
        var counts = new GridCounts(rows.Length, rows.Count(row => row.Days.Any(day => day.Shifts.Any(shift => shift.Status == ShiftStatus.Submitted))),
            rows.Count(row => row.Days.Any(day => day.Missing)), rows.Count(row => row.Warnings));
        var filtered = rows.Where(row => query.Filter switch
        {
            "awaiting" => row.Days.Any(day => day.Shifts.Any(shift => shift.Status == ShiftStatus.Submitted)),
            "missing" => row.Days.Any(day => day.Missing),
            "warnings" => row.Warnings,
            _ => true
        }).ToArray();
        var cutoffDay = await settings.GetIntAsync(new("company.payroll_cutoff_day"), null, cancellationToken);
        var cutoff = PayrollCutoff.Next(today, cutoffDay);
        CutoffBanner? banner = null;
        if (cutoff.DayNumber - today.DayNumber <= 2)
        {
            var periodEnd = new DateOnly(cutoff.Year, cutoff.Month, 1).AddDays(-1);
            var count = await store.AwaitingPeriodAsync(new(periodEnd.Year, periodEnd.Month, 1), periodEnd, cancellationToken);
            if (count > 0) banner = new(cutoff, count);
        }
        var offset = ((long)query.Page - 1) * query.PageSize;
        return new(query.Week, store.CompanyTimeZone, query.Page, query.PageSize, filtered.Length, counts,
            offset >= filtered.Length ? [] : filtered.Skip((int)offset).Take(query.PageSize).ToArray(), banner);
    }

    public async Task<ShiftDetail> HandleAsync(GetShiftDetailQuery query, AccessContext actor, CancellationToken cancellationToken) =>
        await store.DetailAsync(query.ShiftId, clock.GetUtcNow(), cancellationToken) ?? throw new ShiftRuleException("not_found");

    private static WeekRow Row(WeekDriverFacts driver, DateOnly week, DateOnly today, DateTimeOffset now)
    {
        var days = Enumerable.Range(0, 7).Select(offset =>
        {
            var date = week.AddDays(offset);
            var shifts = driver.Shifts.Where(shift => shift.Date == date).ToArray();
            var absences = driver.Absences.Where(absence => absence.From <= date && absence.To >= date).Select(absence => absence.Type).Distinct().ToArray();
            var missing = date < today && driver.CycleDates.Contains(date) && shifts.Length == 0 && absences.Length == 0;
            var minutes = shifts.Sum(shift => WorkMinutes(shift, now));
            var status = missing ? "missing" : shifts.Length > 0 ? Status(shifts) : absences.Length > 0 ? "absence" : "empty";
            var warning = shifts.Any(shift => shift.OdometerLower || shift.DeviceTimeGap || shift.BlockedAtStart is not null || driver.FlaggedShifts.Contains(shift.Id));
            var label = status switch { "missing" => "Missing", "approved" => "Approved", "awaiting" => "Awaiting approval", "rejected" => "Rejected", "running" => "Running", "draft" => "Draft", "absence" => "Absence", _ => "No shift" };
            return new WeekCell(date, minutes, status, label, missing, warning,
                shifts.Select(shift => new GridShift(shift.Id, shift.Version, shift.Status, shift.OdometerLower || shift.DeviceTimeGap || shift.BlockedAtStart is not null || driver.FlaggedShifts.Contains(shift.Id))).ToArray(), absences);
        }).ToArray();
        var rowStatus = days.Any(day => day.Missing) ? "missing" : driver.Shifts.Count > 0 ? Status(driver.Shifts) : days.All(day => day.Absences.Count > 0) ? "absence" : "empty";
        return new(driver.EmployeeId, driver.Driver, driver.PersonnelNumber, days, days.Sum(day => day.WorkMinutes),
            driver.Shifts.SelectMany(shift => shift.Overnights).GroupBy(night => night.CountryCode).Select(group => new NightCount(group.Key, group.Count())).ToArray(), rowStatus, days.Any(day => day.Warnings));
    }

    private static string Status(IEnumerable<Shift> shifts)
    {
        var statuses = shifts.Select(shift => shift.Status).ToArray();
        if (statuses.Contains(ShiftStatus.Running)) return "running";
        if (statuses.Contains(ShiftStatus.Rejected)) return "rejected";
        if (statuses.Contains(ShiftStatus.Submitted)) return "awaiting";
        return statuses.All(status => status is ShiftStatus.Approved or ShiftStatus.Exported) ? "approved" : "draft";
    }
    private static int WorkMinutes(Shift shift, DateTimeOffset now) =>
        (int)((ShiftRules.Hours(shift, ActivityType.Driving, now) + ShiftRules.Hours(shift, ActivityType.OtherWork, now)) * 60m);
}
