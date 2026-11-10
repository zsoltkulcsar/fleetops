using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfTimesheetAlertFacts : ITimesheetAlertFacts
{
    public async Task<TimesheetAlertFacts?> ReadAsync(Guid shiftId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        var shift = await db.Shifts.AsNoTracking().Where(row => row.Id == shiftId)
            .Select(row => new { row.Status, row.StartAt, row.SubmittedAt }).SingleOrDefaultAsync(cancellationToken);
        return shift is null ? null : new(shift.Status.ToString(), shift.StartAt, shift.SubmittedAt);
    }
}
