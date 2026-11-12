using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
namespace FleetOps.Modules.Alerts;
public sealed class DriverAlertNotifications(INotifications notifications, IDriverAlertRecipient recipients) : IAlertOpened
{
    public async Task HandleAsync(string ruleId, string subjectType, Guid subjectId, Guid alertId, CommandContext context, CancellationToken cancellationToken)
    {
        // Only initial openings notify drivers; chasing and escalation stay with office roles.
        if (subjectType == "employee" && ruleId is "A-02" or "A-09" or "A-15")
        {
            var template = ruleId switch { "A-02" => "critical_alert", "A-09" => "missing_shift", _ => "week_reminder" };
            await notifications.AddAsync(subjectId, template, new { alertId, ruleId, documentType = ruleId == "A-02" ? "Document" : null }, alertId, context, cancellationToken);
        }
        if (subjectType == "shift" && ruleId == "A-16" && await recipients.ShiftOwnerAsync(subjectId, context, cancellationToken) is Guid employeeId)
            await notifications.AddAsync(employeeId, "shift_still_running", new { alertId, shiftId = subjectId }, alertId, context, cancellationToken);
    }
}
