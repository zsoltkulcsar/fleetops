using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
namespace FleetOps.Modules.Notifications;
public sealed class NotificationJobHandler(INotificationJobStore store, INotifications notifications, ISettings settings, TimeProvider time, ICompanyTimeService companyTime) : ICommandHandler<RunNotificationJobCommand, NotificationJobResult>
{
    public async Task<NotificationJobResult> HandleAsync(RunNotificationJobCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (command.Kind == NotificationJobKind.Digest)
        {
            if (companyTime.ToCompanyTime(now).Hour < 7) return new(true, null, 0);
            var today = companyTime.ToCompanyDate(now);
            var users = await store.DigestUsersAsync(command.Cursor, context, cancellationToken);
            foreach (var employeeId in users)
            {
                var alerts = await store.DigestAlertsAsync(employeeId, today, context, cancellationToken);
                if (alerts.Count == 0) continue;
                var groups = alerts.GroupBy(alert => alert.RuleId).OrderBy(group => group.Key).Select(group => new { ruleId = group.Key, count = group.Count(), top = group.Take(3).Select(alert => alert.Id).ToArray() }).ToArray();
                await store.QueueDigestAsync(employeeId, today, new { groups }, context, cancellationToken);
            }
            return new(users.Count < 100, users.Count < 100 ? null : users[^1].ToString(), users.Count);
        }
        var minutes = await settings.GetIntAsync(new("alerts.escalation_minutes"), context, cancellationToken);
        if (minutes <= 0) throw new InvalidOperationException("escalation_setting_invalid");
        var candidates = await store.LockEscalationsAsync(now.AddMinutes(-minutes), command.Cursor, context, cancellationToken);
        foreach (var alert in candidates)
        {
            var nextRole = alert.AssignedRole == "hr" ? "dispatcher" : "hr";
            var recipients = await store.OfficeRecipientsAsync(["owner", nextRole], context, cancellationToken);
            if (recipients.Count == 0) continue;
            foreach (var employeeId in recipients)
                await notifications.AddAsync(employeeId, "alert_escalated", new { alertId = alert.Id, ruleId = alert.RuleId }, alert.Id, context, cancellationToken);
            await store.MarkEscalatedAsync(alert.Id, now, context, cancellationToken);
        }
        return new(candidates.Count < 100, candidates.Count < 100 ? null : candidates[^1].Id.ToString(), candidates.Count);
    }
}
