using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class NotificationPreferencesStore(IConfiguration configuration) : INotificationPreferencesStore
{
    public async Task<NotifyPreferences?> GetAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration);
        return await db.Users.AsNoTracking().Where(user => user.EmployeeId == employeeId)
            .Select(user => new NotifyPreferences(user.CriticalPush, user.CriticalEmail, user.DigestEmail)).SingleOrDefaultAsync(cancellationToken);
    }
    public async Task<bool> SaveAsync(Guid employeeId, NotifyPreferences preferences, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        return await db.Users.Where(user => user.EmployeeId == employeeId).ExecuteUpdateAsync(update => update
            .SetProperty(user => user.CriticalPush, preferences.CriticalPush)
            .SetProperty(user => user.CriticalEmail, preferences.CriticalEmail)
            .SetProperty(user => user.DigestEmail, preferences.DigestEmail), cancellationToken) == 1;
    }
}
