using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Migrations;
using Microsoft.AspNetCore.Http.HttpResults;
using Npgsql;

namespace FleetOps.Api.Jobs;

public sealed record SystemActor(string Name);
public sealed record JobContext(string? Cursor, DateTimeOffset Now, SystemActor Actor);
public sealed record JobStep(bool Done, string? Cursor, int Processed);
public interface IJob
{
    string Name { get; }
    Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken);
}

[VisibleTo(RoleCode.Owner)]
public sealed record JobRunResult(string Status, int Processed, string? ErrorType = null);

public sealed class JobRunner(IConfiguration configuration, IEnumerable<IJob> jobs, TimeProvider time)
{
    private readonly IReadOnlyDictionary<string, IJob> jobs = jobs.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);

    public bool Contains(string name) => jobs.ContainsKey(name);

    public Task<JobRunResult> RunAsync(string name, JobTrigger trigger, CancellationToken cancellationToken) =>
        RunAsync(name, trigger, time.GetUtcNow().AddSeconds(configuration.GetValue("Jobs:BudgetSeconds", 50)), cancellationToken);

    public async Task<JobRunResult> RunNightlyAsync(JobTrigger trigger, CancellationToken cancellationToken)
    {
        var budget = time.GetUtcNow().AddSeconds(configuration.GetValue("Jobs:BudgetSeconds", 50));
        var order = configuration.GetSection("Jobs:NightlyOrder").Get<string[]>() ?? [];
        var names = order.Length == 0
            ? jobs.Keys
            : order.Distinct(StringComparer.OrdinalIgnoreCase).Where(Contains);
        var processed = 0;
        var status = "succeeded";
        foreach (var name in names)
        {
            var result = await RunAsync(name, trigger, budget, cancellationToken);
            processed += result.Processed;
            if (result.Status == "failed") status = "failed";
            else if (result.Status is "partial" or "skipped") status = status == "succeeded" ? result.Status : status;
            if (time.GetUtcNow() >= budget) break;
        }
        return new(status, processed);
    }

    private async Task<JobRunResult> RunAsync(string name, JobTrigger trigger, DateTimeOffset budget, CancellationToken cancellationToken)
    {
        if (!jobs.TryGetValue(name, out var job)) throw new KeyNotFoundException(name);
        var connectionString = DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.");
        var connectionBuilder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        var locked = await TryLockAsync(connection, name, cancellationToken);
        if (!locked)
        {
            var skipped = await InsertRunAsync(connection, name, trigger, JobStatus.Skipped, 0, null, time.GetUtcNow(), time.GetUtcNow(), cancellationToken, $"system:{name}");
            return new("skipped", 0);
        }
        Guid runId = Guid.Empty;
        var processed = 0;
        var actor = new SystemActor($"system:{name}");
        try
        {
            var started = time.GetUtcNow();
            runId = await InsertRunAsync(connection, name, trigger, JobStatus.Running, 0, null, started, null, cancellationToken, actor.Name);
            var cursor = await LoadCursorAsync(connection, name, cancellationToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var step = await job.RunStepAsync(new JobContext(cursor, time.GetUtcNow(), actor), cancellationToken);
                processed += step.Processed;
                cursor = step.Cursor;
                await SaveCursorAsync(connection, name, cursor, step.Done, cancellationToken);
                if (step.Done)
                {
                    await FinishRunAsync(connection, runId, JobStatus.Succeeded, processed, null, time.GetUtcNow(), cancellationToken);
                    return new("succeeded", processed);
                }
                if (time.GetUtcNow() >= budget)
                {
                    await FinishRunAsync(connection, runId, JobStatus.Partial, processed, null, time.GetUtcNow(), cancellationToken);
                    return new("partial", processed);
                }
            }
        }
        catch (Exception exception)
        {
            if (runId != Guid.Empty)
                await FinishRunAsync(connection, runId, JobStatus.Failed, processed, exception.GetType().Name, time.GetUtcNow(), CancellationToken.None);
            return new("failed", processed, exception.GetType().Name);
        }
        finally
        {
            await UnlockAsync(connection, name, CancellationToken.None);
        }
    }

    private static async Task<bool> TryLockAsync(NpgsqlConnection connection, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select pg_try_advisory_lock(hashtextextended('fleetops.job.' || $1, 0))", connection);
        command.Parameters.AddWithValue(name);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task UnlockAsync(NpgsqlConnection connection, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select pg_advisory_unlock(hashtextextended('fleetops.job.' || $1, 0))", connection);
        command.Parameters.AddWithValue(name);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<Guid> InsertRunAsync(NpgsqlConnection connection, string name, JobTrigger trigger, JobStatus status, int processed, string? errorType, DateTimeOffset started, DateTimeOffset? finished, CancellationToken cancellationToken, string actorName = "")
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand("insert into fleetops.job_runs (id, name, trigger, actor_name, started_at, finished_at, status, processed, error_type) values ($1, $2, $3, $4, $5, $6, $7, $8, $9)", connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(trigger.ToString());
        command.Parameters.AddWithValue(actorName);
        command.Parameters.AddWithValue(started);
        command.Parameters.AddWithValue(finished is null ? DBNull.Value : finished.Value);
        command.Parameters.AddWithValue(status.ToString());
        command.Parameters.AddWithValue(processed);
        command.Parameters.AddWithValue(errorType is null ? DBNull.Value : errorType);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return id;
    }

    private static async Task FinishRunAsync(NpgsqlConnection connection, Guid id, JobStatus status, int processed, string? errorType, DateTimeOffset finished, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("update fleetops.job_runs set finished_at = $2, status = $3, processed = $4, error_type = $5 where id = $1", connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(finished);
        command.Parameters.AddWithValue(status.ToString());
        command.Parameters.AddWithValue(processed);
        command.Parameters.AddWithValue(errorType is null ? DBNull.Value : errorType);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> LoadCursorAsync(NpgsqlConnection connection, string name, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select cursor from fleetops.job_checkpoints where name = $1", connection);
        command.Parameters.AddWithValue(name);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task SaveCursorAsync(NpgsqlConnection connection, string name, string? cursor, bool done, CancellationToken cancellationToken)
    {
        if (done || cursor is null)
        {
            await using var delete = new NpgsqlCommand("delete from fleetops.job_checkpoints where name = $1", connection);
            delete.Parameters.AddWithValue(name);
            await delete.ExecuteNonQueryAsync(cancellationToken);
            return;
        }
        await using var command = new NpgsqlCommand("insert into fleetops.job_checkpoints (name, cursor, updated_at) values ($1, $2, $3) on conflict (name) do update set cursor = excluded.cursor, updated_at = excluded.updated_at", connection);
        command.Parameters.AddWithValue(name);
        command.Parameters.AddWithValue(cursor);
        command.Parameters.AddWithValue(DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class FileOrphansJob(IConfiguration configuration, IObjectStore objectStore, TimeProvider time) : IJob
{
    public string Name => "file-orphans";

    public async Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken)
    {
        var connectionString = DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var cutoff = time.GetUtcNow().AddHours(-1);
        await using var command = new NpgsqlCommand("select id, object_key from fleetops.pending_files where confirmed_at is null and expires_at < $1 and ($2::uuid is null or id > $2) order by id limit 50", connection);
        command.Parameters.AddWithValue(cutoff);
        command.Parameters.AddWithValue(context.Cursor is null ? DBNull.Value : Guid.Parse(context.Cursor));
        var rows = new List<(Guid Id, string Key)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetGuid(0), reader.GetString(1)));
        foreach (var row in rows)
        {
            await objectStore.DeleteAsync(row.Key, cancellationToken);
            await using var delete = new NpgsqlCommand("delete from fleetops.pending_files where id = $1 and confirmed_at is null", connection);
            delete.Parameters.AddWithValue(row.Id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        var cursor = rows.Count == 0 ? context.Cursor : rows[^1].Id.ToString();
        return new(rows.Count < 50, cursor, rows.Count);
    }
}

[CommandPolicy("jobs.trigger")]
public sealed record RunJobCommand(string Name) : ICommand<JobRunResult>;
public sealed class RunJobCommandHandler(JobRunner runner) : ICommandHandler<RunJobCommand, JobRunResult>
{
    public Task<JobRunResult> HandleAsync(RunJobCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken) => runner.RunAsync(command.Name, JobTrigger.admin, cancellationToken);
}

[QueryPolicy("jobs.trigger")]
public sealed record ListJobsQuery : IQuery<JobListResult>;

[VisibleTo(RoleCode.Owner)]
public sealed record JobListResult(IReadOnlyList<JobListItem> Items) : IReadModel;
public sealed record JobListItem(string Name, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? Status, int Processed);

public sealed class ListJobsQueryHandler(IConfiguration configuration, IEnumerable<IJob> jobs) : IQueryHandler<ListJobsQuery, JobListResult>
{
    public async Task<JobListResult> HandleAsync(ListJobsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var connectionString = DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select names.name, runs.started_at, runs.finished_at, runs.status, runs.processed from (select distinct name from unnest($1::text[]) as registered(name)) names left join lateral (select started_at, finished_at, status, processed from fleetops.job_runs where name = names.name order by started_at desc limit 1) runs on true order by names.name", connection);
        command.Parameters.AddWithValue(jobs.Select(job => job.Name).ToArray());
        var items = new List<JobListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1), reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? 0 : reader.GetInt32(4)));
        return new(items);
    }
}
