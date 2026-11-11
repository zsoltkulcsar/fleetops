using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfPlanningAlertFacts : IPlanningAlertFacts
{
    public async Task<bool> OpenScheduleRequestAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        return await db.ScheduleChangeRequests.AnyAsync(request => request.Id == id && request.Status == ScheduleRequestStatus.Open, cancellationToken);
    }
    public async Task<bool> ShiftDuringAbsenceAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        if (await db.Alerts.AnyAsync(alert => alert.SubjectType == "shift" && alert.SubjectId == id && alert.RuleId == "A-18" && alert.Status == AlertStatus.Resolved && alert.ResolvedBy != null, cancellationToken)) return false;
        var receipt = await db.CommandReceipts.Where(receipt => receipt.AggregateId == id && receipt.Type == "shift.start" && receipt.Outcome == "applied").Select(receipt => receipt.Result).FirstOrDefaultAsync(cancellationToken);
        if (receipt is null) return false;
        using var result = System.Text.Json.JsonDocument.Parse(receipt);
        if (!result.RootElement.TryGetProperty("absenceType", out var absenceType) || absenceType.ValueKind != System.Text.Json.JsonValueKind.String) return false;
        return await db.Shifts.AnyAsync(shift => shift.Id == id && db.Absences.Any(absence => absence.EmployeeId == shift.EmployeeId && absence.Status == AbsenceStatus.Approved && absence.From <= shift.Date && absence.To >= shift.Date), cancellationToken);
    }
}
