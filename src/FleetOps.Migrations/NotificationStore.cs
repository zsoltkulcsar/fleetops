using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FleetOps.Kernel;

namespace FleetOps.Migrations;

public sealed class EfNotifications(TimeProvider time, INotificationIds ids) : INotifications
{
    public async Task<Guid> AddAsync(Guid employeeId, string templateCode, object parameters, Guid? alertId, CommandContext context, CancellationToken cancellationToken)
    {
        if (templateCode is not ("document_rejected" or "critical_alert" or "schedule_changed" or "sync_conflict" or "week_reminder" or "shift_rejected" or "shift_edited_by_office")) throw new ArgumentException("Unknown notification template.", nameof(templateCode));
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand("insert into fleetops.notifications (id, employee_id, alert_id, template_code, parameters, channel, created_at, attempts) values ($1,$2,$3,$4,$5::jsonb,'in_app',$6,0)", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
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

public sealed class InAppSender(IConfiguration configuration, TimeProvider time, ILogger<InAppSender> logger) : INotificationSender, IPostCommitNotifications
{
    public NotificationChannel Channel => NotificationChannel.InApp;

    public async Task SendAsync(IReadOnlyCollection<Guid> ids, CommandContext context, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return;
        await using var command = new NpgsqlCommand("update fleetops.notifications set sent_at = $1 where id = any($2::uuid[]) and channel = 'in_app' and sent_at is null", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        command.Parameters.AddWithValue(time.GetUtcNow());
        command.Parameters.AddWithValue(ids.ToArray());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TrySendAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0) return;
        try
        {
            var connectionString = Resolve(configuration);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await SendAsync(ids, new CommandContext(connection, transaction), cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Notification send failed. ExceptionType={ExceptionType}", exception.GetType().Name);
            await RecordFailureAsync(ids, cancellationToken);
        }
    }

    private async Task RecordFailureAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        try
        {
            var connectionString = Resolve(configuration);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("update fleetops.notifications set attempts = attempts + 1, next_attempt_at = $1 + case when attempts = 0 then interval '1 minute' when attempts = 1 then interval '5 minutes' else interval '30 minutes' end where id = any($2::uuid[]) and sent_at is null", connection);
            command.Parameters.AddWithValue(time.GetUtcNow());
            command.Parameters.AddWithValue(ids.ToArray());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) { logger.LogWarning("Notification failure recording failed. ExceptionType={ExceptionType}", exception.GetType().Name); }
    }

    private static string Resolve(IConfiguration configuration) => configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured.");
}

public sealed class NotificationQueryHandler(IConfiguration configuration) : IQueryHandler<ListNotificationsQuery, NotificationListResult>
{
    public async Task<NotificationListResult> HandleAsync(ListNotificationsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select id, template_code, parameters, created_at, read_at from fleetops.notifications where employee_id = $1 and ($2::uuid is null or id < $2) order by id desc limit $3", connection);
        command.Parameters.AddWithValue(actor.EmployeeId);
        command.Parameters.AddWithValue(query.Cursor is null ? DBNull.Value : Guid.Parse(query.Cursor));
        command.Parameters.AddWithValue(Math.Clamp(query.Limit, 1, 100));
        var items = new List<NotificationItem>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetGuid(0);
                var template = reader.GetString(1);
                var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(reader.GetString(2)) ?? [];
                var text = NotificationTemplates.Render(template, parameters, "en");
                var link = template is "week_reminder" or "shift_rejected" or "shift_edited_by_office" ? "/driver/#/week" : template == "sync_conflict" ? (actor.Roles.Contains(RoleCode.Driver) ? "/driver/#/week" : "/attention") : template == "schedule_changed" ? "/driver/#/schedule" : template == "document_rejected" && parameters.TryGetValue("documentId", out var documentId) ? (actor.Roles.Contains(RoleCode.Driver) ? $"/driver/#/me?open={documentId.GetString()}" : $"/documents?open={documentId.GetString()}") : "/";
                items.Add(new(id, template, text, reader.GetFieldValue<DateTimeOffset>(3), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4), link));
            }
        }
        await using var unreadCommand = new NpgsqlCommand("select count(*) from fleetops.notifications where employee_id = $1 and read_at is null", connection);
        unreadCommand.Parameters.AddWithValue(actor.EmployeeId);
        var unread = Convert.ToInt32(await unreadCommand.ExecuteScalarAsync(cancellationToken));
        return new(items, unread, items.Count == query.Limit ? items[^1].Id.ToString() : null);
    }
}

public sealed class MarkNotificationReadHandler(TimeProvider time) : ICommandHandler<MarkNotificationReadCommand, bool>
{
    public async Task<bool> HandleAsync(MarkNotificationReadCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand("update fleetops.notifications set read_at = coalesce(read_at, $1) where id = $2 and employee_id = $3 returning id", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        update.Parameters.AddWithValue(time.GetUtcNow()); update.Parameters.AddWithValue(command.Id); update.Parameters.AddWithValue(actor.EmployeeId);
        return await update.ExecuteScalarAsync(cancellationToken) is Guid;
    }
}

public sealed class MarkAllNotificationsReadHandler(TimeProvider time) : ICommandHandler<MarkAllNotificationsReadCommand, int>
{
    public async Task<int> HandleAsync(MarkAllNotificationsReadCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var update = new NpgsqlCommand("update fleetops.notifications set read_at = $1 where employee_id = $2 and read_at is null", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        update.Parameters.AddWithValue(time.GetUtcNow()); update.Parameters.AddWithValue(actor.EmployeeId);
        return await update.ExecuteNonQueryAsync(cancellationToken);
    }
}
