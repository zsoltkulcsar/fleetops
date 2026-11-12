using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace FleetOps.Migrations;
public sealed class PushSubscriptionStore(IConfiguration configuration) : IPushSubscriptionStore
{
    public async Task SaveAsync(Guid employeeId, string endpoint, string p256dh, string auth, CommandContext context, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("insert into fleetops.push_subscriptions (id, employee_id, endpoint, p256dh, auth) values ($1,$2,$3,$4,$5) on conflict (employee_id, endpoint) do update set p256dh = excluded.p256dh, auth = excluded.auth", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        command.Parameters.AddWithValue(Guid.CreateVersion7());
        command.Parameters.AddWithValue(employeeId);
        command.Parameters.AddWithValue(endpoint);
        command.Parameters.AddWithValue(p256dh);
        command.Parameters.AddWithValue(auth);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<bool> DeleteAsync(Guid employeeId, string endpoint, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        return await db.PushSubscriptions.Where(subscription => subscription.EmployeeId == employeeId && subscription.Endpoint == endpoint).ExecuteDeleteAsync(cancellationToken) > 0;
    }
}
