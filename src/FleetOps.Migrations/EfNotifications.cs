using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
using Npgsql;
namespace FleetOps.Migrations;
// The original in-app persistence port remains available to existing integrations.
public sealed class EfNotifications(TimeProvider time, INotificationIds ids) : INotifications
{
    public async Task<Guid> AddAsync(Guid employeeId, string templateCode, object parameters, Guid? alertId, CommandContext context, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand("insert into fleetops.notifications (id, employee_id, alert_id, template_code, parameters, channel, created_at, attempts, next_attempt_at) values ($1,$2,$3,$4,$5::jsonb,'in_app',$6,0,$6)", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(employeeId);
        command.Parameters.AddWithValue(alertId is null ? DBNull.Value : alertId.Value);
        command.Parameters.AddWithValue(templateCode);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(parameters));
        command.Parameters.AddWithValue(time.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
        ids.Add(id);
        return id;
    }
}
