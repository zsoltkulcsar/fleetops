using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class NotificationDeliveryStore(IConfiguration configuration, TimeProvider time, ICompanyTimeService companyTime, ILogger<NotificationDeliveryStore> logger) : INotificationDeliveryStore
{
    public async Task DeliverAsync(Guid id, Func<PendingDelivery, Task<DeliveryOutcome?>> send, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var notification = await db.Notifications.FromSqlInterpolated($"select * from fleetops.notifications where id = {id} for update").SingleOrDefaultAsync(cancellationToken);
        if (notification is null) return;
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == notification.EmployeeId, cancellationToken);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.EmployeeId == notification.EmployeeId, cancellationToken);
        var subscriptions = await db.PushSubscriptions.AsNoTracking().Where(subscription => subscription.EmployeeId == notification.EmployeeId).ToListAsync(cancellationToken);
        var active = employee is not null && employee.Status == FleetOps.People.EmployeeStatus.Active && (employee.EndDate is null || employee.EndDate >= today);
        var roles = employee is null ? [] : JsonSerializer.Deserialize<string[]>(employee.Roles) ?? [];
        var office = user is not null && roles.Any(role => role is "hr" or "dispatcher" or "owner");
        var preferences = user is null ? null : new NotifyPreferences(user.CriticalPush, user.CriticalEmail, user.DigestEmail);
        var outcome = await send(new(notification.Id, notification.EmployeeId, notification.TemplateCode, notification.Parameters, notification.Channel, employee?.LanguagePref ?? "en", user?.Email, subscriptions, active, office, preferences, notification.Attempts, notification.SentAt, notification.NextAttemptAt));
        if (outcome is null) return;
        notification.SentAt = outcome.SentAt;
        notification.Attempts = outcome.Attempts;
        notification.NextAttemptAt = outcome.NextAttemptAt;
        if (outcome.ErrorType is not null)
            logger.LogWarning("Notification delivery failed. NotificationId={NotificationId} ExceptionType={ExceptionType}", notification.Id, outcome.ErrorType);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
