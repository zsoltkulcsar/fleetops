using FleetOps.Kernel;

namespace FleetOps.Modules.Payroll;

public sealed record PayrollActivityFact(string Type, DateTimeOffset StartAt, DateTimeOffset? EndAt);
public sealed record PayrollNightFact(DateOnly Date, string Country);
public sealed record PayrollAbsenceFact(DateOnly From, DateOnly To, string Type, bool HalfDay);

public static class PayrollFactsBuilder
{
    public static PayrollPerson Person(Guid id, string name, string? personnelNumber, DateOnly? endDate, DateOnly month,
        IReadOnlySet<DateOnly> scheduled, IReadOnlySet<DateOnly> holidays, IReadOnlyList<PayrollShiftFact> shifts, IReadOnlyList<PayrollAbsenceFact> absences)
    {
        var missing = scheduled.Where(date => !holidays.Contains(date) && !shifts.Any(shift => shift.Days.FirstOrDefault()?.Date == date) &&
            !absences.Any(absence => !absence.HalfDay && absence.From <= date && absence.To >= date)).Order().ToArray();
        var waiting = shifts.Count(shift => shift.Status is "Submitted" or "Draft" or "Rejected" or "Running");
        return new(id, name, personnelNumber, shifts.Count(shift => shift.Status == "Approved"), waiting, missing,
            endDate >= month && endDate < month.AddMonths(1), waiting == 0 && missing.Length == 0);
    }

    public static IReadOnlyList<PayrollDay> ShiftDays(Guid id, string name, string? personnelNumber, DateOnly date,
        IReadOnlyList<PayrollActivityFact> activities, IReadOnlyList<PayrollNightFact> nights, IReadOnlySet<DateOnly> holidays, TimeZoneInfo timezone)
    {
        var companyTime = new CompanyTimeService(timezone);
        var days = new Dictionary<DateOnly, PayrollDay> { [date] = Empty(id, name, personnelNumber, date, holidays) };
        foreach (var activity in activities.Where(activity => activity.EndAt.HasValue && activity.Type != "Break"))
        {
            var cursor = activity.StartAt;
            var end = activity.EndAt!.Value;
            while (cursor < end)
            {
                var localDate = companyTime.ToCompanyDate(cursor);
                var midnight = localDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
                var boundary = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, timezone), TimeSpan.Zero);
                var stop = boundary < end ? boundary : end;
                var hours = (decimal)(stop - cursor).Ticks / TimeSpan.TicksPerHour;
                var day = days.GetValueOrDefault(localDate) ?? Empty(id, name, personnelNumber, localDate, holidays);
                days[localDate] = day with
                {
                    DrivingHours = day.DrivingHours + (activity.Type == "Driving" ? hours : 0),
                    OtherWorkHours = day.OtherWorkHours + (activity.Type == "OtherWork" ? hours : 0),
                    AvailabilityHours = day.AvailabilityHours + (activity.Type == "Availability" ? hours : 0)
                };
                cursor = stop;
            }
        }
        foreach (var night in nights)
        {
            var day = days.GetValueOrDefault(night.Date) ?? Empty(id, name, personnelNumber, night.Date, holidays);
            var counts = day.Nights.ToDictionary(item => item.Key, item => item.Value);
            counts[night.Country] = counts.GetValueOrDefault(night.Country) + 1;
            days[night.Date] = day with { Nights = counts };
        }
        return days.Values.OrderBy(day => day.Date).ToArray();
    }

    public static IReadOnlyList<PayrollDay> AbsenceDays(Guid id, string name, string? personnelNumber, DateOnly month,
        DateOnly startDate, DateOnly? endDate, IReadOnlySet<DateOnly> scheduled, IReadOnlySet<DateOnly> holidays, IReadOnlyList<PayrollAbsenceFact> absences)
    {
        return Enumerable.Range(0, DateTime.DaysInMonth(month.Year, month.Month)).Select(month.AddDays)
            .Where(date => date >= startDate && (endDate is null || date <= endDate) && scheduled.Contains(date) && !holidays.Contains(date))
            .SelectMany(date => absences.Where(absence => absence.From <= date && absence.To >= date).Select(absence =>
                Empty(id, name, personnelNumber, date, holidays) with { Absences = new Dictionary<string, decimal> { [absence.Type] = absence.HalfDay ? 0.5m : 1m } })).ToArray();
    }

    private static PayrollDay Empty(Guid id, string name, string? personnelNumber, DateOnly date, IReadOnlySet<DateOnly> holidays) =>
        new(id, personnelNumber, name, date, 0, 0, 0, new Dictionary<string, decimal>(), new Dictionary<string, decimal>(), date.DayOfWeek == DayOfWeek.Sunday, holidays.Contains(date));
}
