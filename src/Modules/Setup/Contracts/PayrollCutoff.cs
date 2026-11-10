namespace FleetOps.Modules.Setup;

public static class PayrollCutoff
{
    public static DateOnly Next(DateOnly today, int day)
    {
        if (day is < 1 or > 31) throw new InvalidOperationException("invalid_payroll_cutoff_day");
        var cutoff = new DateOnly(today.Year, today.Month, Math.Min(day, DateTime.DaysInMonth(today.Year, today.Month)));
        if (cutoff >= today) return cutoff;
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        return new(nextMonth.Year, nextMonth.Month, Math.Min(day, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));
    }
}
