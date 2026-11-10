using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfShiftReminderFacts : IShiftReminderFacts
{
    public async Task<ShiftReminderFacts> ReadAsync(Guid employeeId, DateOnly today, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        IsInCycle membership = new EfIsInCycle(db);
        var from = today.AddDays(-28);
        var candidates = Enumerable.Range(0, 28).Select(offset => from.AddDays(offset)).ToArray();
        var expected = await membership.ForEmployeeDatesAsync(employeeId, candidates, cancellationToken);
        var shifts = await db.Shifts.AsNoTracking().Where(shift => shift.EmployeeId == employeeId && shift.Date >= from && shift.Date <= today)
            .Select(shift => new { shift.Date, shift.Status }).ToListAsync(cancellationToken);
        var absences = await db.Absences.AsNoTracking().Where(absence => absence.EmployeeId == employeeId && absence.Status == AbsenceStatus.Approved && absence.From <= today && absence.To >= from)
            .Select(absence => new { absence.From, absence.To }).ToListAsync(cancellationToken);
        var missing = expected.Where(date => !shifts.Any(shift => shift.Date == date) && !absences.Any(absence => absence.From <= date && absence.To >= date)).Order().ToArray();
        var monday = today.AddDays(-((int)today.DayOfWeek + 6) % 7);
        var previous = monday.AddDays(-7);
        return new(await membership.ForEmployeeAsync(employeeId, today, cancellationToken), missing,
            shifts.Any(shift => shift.Date >= previous && shift.Date < monday && shift.Status is ShiftStatus.Draft or ShiftStatus.Rejected), previous);
    }
}
