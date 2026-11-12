using System.Text.Json;

namespace FleetOps.Modules.Payroll;

public static class PayrollViews
{
    public static PayrollExportView From(PayrollExport export) => new(export.Id, export.PeriodFrom, export.PeriodTo,
        export.Format, export.Kind, export.Status, export.Version, export.CreatedBy, export.CreatedAt, export.SentDate,
        export.Note, export.Reason, export.CorrectionOf, export.Checksum,
        JsonSerializer.Deserialize<PayrollShiftVersion[]>(export.ShiftVersionsJson)!, JsonSerializer.Deserialize<PayrollDelta[]>(export.DeltasJson)!);

    public static void ValidateMonth(DateOnly month)
    {
        if (month == default || month.Day != 1 || month.Year >= 9999) throw new PayrollValidationException("month");
    }

    public static IReadOnlyList<PayrollDay> Combine(IEnumerable<PayrollDay> days) => days.GroupBy(day => new { day.EmployeeId, day.Date })
        .OrderBy(group => group.Key.EmployeeId).ThenBy(group => group.Key.Date).Select(group => group.First() with
        {
            DrivingHours = group.Sum(day => day.DrivingHours),
            OtherWorkHours = group.Sum(day => day.OtherWorkHours),
            AvailabilityHours = group.Sum(day => day.AvailabilityHours),
            Nights = Sum(group.Select(day => day.Nights)),
            Absences = Sum(group.Select(day => day.Absences))
        }).ToArray();

    public static IReadOnlyDictionary<string, decimal> Sum(IEnumerable<IReadOnlyDictionary<string, decimal>> values) =>
        values.SelectMany(value => value).GroupBy(value => value.Key).OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Sum(value => value.Value));

    public static PayrollPreview Preview(DateOnly month, PayrollMonthFacts facts)
    {
        var approved = facts.Shifts.Where(shift => shift.Status == "Approved").OrderBy(shift => shift.ShiftId).ToArray();
        var days = Combine(approved.SelectMany(shift => shift.Days).Concat(facts.Absences));
        return new(month, approved.Select(shift => new PayrollShiftVersion(shift.ShiftId, shift.Version)).ToArray(), days,
            days.GroupBy(day => day.EmployeeId).Select(group => new PayrollTotal(group.Key, group.First().Name,
                group.Sum(day => day.DrivingHours), group.Sum(day => day.OtherWorkHours), group.Sum(day => day.AvailabilityHours),
                Sum(group.Select(day => day.Nights)), Sum(group.Select(day => day.Absences)))).ToArray());
    }
}
