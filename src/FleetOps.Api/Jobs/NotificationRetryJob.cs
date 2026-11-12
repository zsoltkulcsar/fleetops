using FleetOps.Migrations;
using FleetOps.Modules.Notifications;
using FleetOps.Modules.Setup;
using Npgsql;

namespace FleetOps.Api.Jobs;

public sealed class NotificationRetryJob(IConfiguration configuration, ISettings settings, IPostCommitNotifications sender, ILogger<NotificationRetryJob> logger) : IJob
{
    public string Name => "notifications-retry";

    public async Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured.");
        var maxAttempts = await settings.GetIntAsync(new SettingKey("notifications.max_attempts"), null, cancellationToken);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select id, attempts from fleetops.notifications where sent_at is null and (next_attempt_at is null or next_attempt_at <= case when channel = 'in_app' then now() else $2 end) and ($1::uuid is null or id > $1) order by id limit 100", connection);
        command.Parameters.AddWithValue(context.Cursor is null ? DBNull.Value : Guid.Parse(context.Cursor));
        command.Parameters.AddWithValue(context.Now);
        var rows = new List<(Guid Id, int Attempts)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetGuid(0), reader.GetInt32(1)));
        var sendable = rows.Where(x => x.Attempts < maxAttempts).Select(x => x.Id).ToArray();
        await sender.TrySendAsync(sendable, cancellationToken);
        foreach (var row in rows.Where(x => x.Attempts >= maxAttempts)) logger.LogWarning("Notification retry limit reached. NotificationId={NotificationId}", row.Id);
        var cursor = rows.Count == 0 ? context.Cursor : rows[^1].Id.ToString();
        return new(rows.Count < 100, cursor, rows.Count);
    }
}
