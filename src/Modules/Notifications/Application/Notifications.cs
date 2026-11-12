using System.Text.Json;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Notifications;
public sealed class Notifications(INotificationQueueStore store, IPushConfiguration pushConfiguration) : INotifications
{
    public async Task<Guid> AddAsync(Guid employeeId, string templateCode, object parameters, Guid? alertId, CommandContext context, CancellationToken cancellationToken)
    {
        if (templateCode is not ("correction_done" or "correction_declined" or "vacation_decided" or "absence_recorded" or "schedule_request_decided" or "vehicle_release_review" or "doc_expiring" or "expiry_warning" or "assignment_changed" or "vehicle_unusable" or "defect_cancelled" or "document_rejected" or "critical_alert" or "schedule_changed" or "sync_conflict" or "week_reminder" or "submit_reminder" or "missing_shift" or "shift_still_running" or "shift_rejected" or "shift_edited_by_office" or "alert_escalated"))
            throw new ArgumentException("Unknown notification template.", nameof(templateCode));
        var critical = templateCode is "critical_alert" or "alert_escalated";
        var recipients = await store.RecipientsAsync(employeeId, critical || templateCode == "expiry_warning", context, cancellationToken);
        var firstId = Guid.Empty;
        var payload = JsonSerializer.SerializeToElement(parameters);
        var immediate = templateCode == "alert_escalated" || (critical && payload.TryGetProperty("ruleId", out var rule) && rule.GetString() is "A-02" or "A-05" or "A-07");
        foreach (var recipient in recipients)
        {
            var id = await store.InsertAsync(recipient.EmployeeId, templateCode, parameters, alertId, NotificationChannel.InApp, context, cancellationToken);
            if (firstId == Guid.Empty) firstId = id;
            if (!immediate || !recipient.Office) continue;
            if (recipient.Preferences.CriticalEmail)
                await store.InsertAsync(recipient.EmployeeId, templateCode, parameters, alertId, NotificationChannel.Email, context, cancellationToken);
            if (recipient.Preferences.CriticalPush && recipient.Subscribed && pushConfiguration.Get().Enabled)
                await store.InsertAsync(recipient.EmployeeId, templateCode, parameters, alertId, NotificationChannel.Push, context, cancellationToken);
        }
        return firstId;
    }
}
