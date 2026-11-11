using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Absences;
public static class AbsenceRules
{
    public static void Validate(DateOnly from, DateOnly to, HalfDay? halfDay, AbsenceType type, string? note)
    {
        if (from == default || to < from || to.DayNumber - from.DayNumber > 3660) throw new AbsenceValidationException("from", "to");
        if (!Enum.IsDefined(type)) throw new AbsenceValidationException("type");
        if (halfDay is not null && (!Enum.IsDefined(halfDay.Value) || from != to)) throw new AbsenceValidationException("halfDay");
        if (note?.Length > 2000 || (type == AbsenceType.Sick && note is not null)) throw new AbsenceValidationException("note");
    }
    public static string Reason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new AbsenceValidationException("reason");
        return reason.Trim();
    }
    public static decimal Days(Absence absence, int year, IReadOnlySet<DateOnly> holidays)
    {
        var start = absence.From > new DateOnly(year, 1, 1) ? absence.From : new DateOnly(year, 1, 1);
        var end = absence.To < new DateOnly(year, 12, 31) ? absence.To : new DateOnly(year, 12, 31);
        decimal days = 0;
        for (var number = start.DayNumber; number <= end.DayNumber; number++)
        {
            var date = DateOnly.FromDayNumber(number);
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !holidays.Contains(date)) days += absence.HalfDay is null ? 1 : 0.5m;
        }
        return days;
    }
    public static VacationBalance Balance(AbsenceFacts facts, int year)
    {
        var opening = facts.OpeningYear == year ? facts.Opening ?? 0 : 0;
        var entitlement = facts.Entitlement ?? 0;
        var used = facts.Absences.Where(absence => absence.Type == AbsenceType.Vacation && absence.Status == AbsenceStatus.Approved).Sum(absence => Days(absence, year, facts.Holidays));
        return new(year, opening, entitlement, used, opening + entitlement - used);
    }
    public static AbsenceView View(Absence absence) => new(absence.Id, absence.EmployeeId, absence.From, absence.To, absence.HalfDay, absence.Type, absence.Status, absence.Note, absence.DeclineReason, absence.CancelReason);
    public static async Task CheckShiftsAsync(IAbsenceStore store, Absence absence, bool confirm, CommandContext context, CancellationToken cancellationToken)
    {
        var shifts = await store.ShiftsAsync(absence.EmployeeId, absence.From, absence.To, context, cancellationToken);
        var running = shifts.Where(shift => shift.Date == absence.From && shift.Status == "running").ToArray();
        if (running.Length > 0) throw new AbsenceConflictException("running_shift", running);
        if (shifts.Count > 0 && !confirm) throw new AbsenceConflictException("absence_overlaps_shifts", shifts);
    }
}
