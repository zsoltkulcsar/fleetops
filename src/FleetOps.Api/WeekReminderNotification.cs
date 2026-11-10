using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Notifications;

namespace FleetOps.Api;

public sealed class WeekReminderNotification(INotifications notifications) : IAlertOpened
{
    public async Task HandleAsync(string ruleId, string subjectType, Guid subjectId, Guid alertId, CommandContext context, CancellationToken cancellationToken)
    {
        if (ruleId == "A-15" && subjectType == "employee")
            await notifications.AddAsync(subjectId, "week_reminder", new { alertId }, alertId, context, cancellationToken);
    }
}
