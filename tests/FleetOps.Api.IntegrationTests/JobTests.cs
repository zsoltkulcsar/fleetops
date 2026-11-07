using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api.Jobs;
using FleetOps.Kernel.Authorization;
using FleetOps.Kernel.Files;
using FleetOps.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class JobTests : IAsyncLifetime
{
    private readonly JobApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Job_runs_with_the_secret_and_records_a_run()
    {
        using var response = await factory.GetJob("recording");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var row = await factory.JobRun("recording");
        Assert.Equal("Succeeded", row.Status);
        Assert.Equal("system:recording", row.ActorName);
        Assert.NotNull(row.FinishedAt);
    }

    [Fact]
    public async Task Job_without_a_secret_is_refused()
    {
        factory.Secret = null;
        using var response = await factory.GetJob("recording");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.JobRunCount("recording"));
    }

    [Fact]
    public async Task Job_with_a_wrong_secret_is_refused()
    {
        using var response = await factory.GetJob("recording", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.JobRunCount("recording"));
    }

    [Fact]
    public async Task Job_with_a_user_session_is_refused()
    {
        using var response = await factory.GetJob("recording", null, signedIn: true);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.JobRunCount("recording"));
    }

    [Fact]
    public async Task Unset_secret_refuses_every_call()
    {
        factory.Secret = null;
        using var response = await factory.GetJob("recording", "");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, await factory.JobRunCount("recording"));
    }

    [Fact]
    public async Task Unknown_job_is_404()
    {
        using var response = await factory.GetJob("missing");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Jobs_are_absent_from_the_public_openapi_document()
    {
        using var response = await factory.Client.GetAsync("/openapi/v1.json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(document.RootElement.GetProperty("paths").EnumerateObject(), path => path.Name.StartsWith("/jobs", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Second_call_during_a_run_is_skipped()
    {
        await using var lockConnection = await factory.DirectConnection();
        await using var lockCommand = new NpgsqlCommand("select pg_advisory_lock(hashtextextended('fleetops.job.' || $1, 0))", lockConnection);
        lockCommand.Parameters.AddWithValue("recording");
        await lockCommand.ExecuteNonQueryAsync();
        using var response = await factory.GetJob("recording");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal("skipped", body.GetProperty("status").GetString());
        Assert.Equal("Skipped", (await factory.JobRun("recording")).Status);
        Assert.Equal(0, factory.RecordingRuns);
    }

    [Fact]
    public async Task Lock_is_held_on_its_own_connection()
    {
        factory.BlockingJob.Release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = factory.GetJob("blocking");
        await factory.BlockingJob.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var lockConnectionPid = await factory.Scalar<long>("select pid::bigint from pg_locks where locktype = 'advisory' and granted and classid is not null limit 1");
        Assert.NotEqual(factory.BlockingJob.DataConnectionPid, lockConnectionPid);
        factory.BlockingJob.Release.SetResult();
        using var response = await run;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Lock_is_released_after_a_failure()
    {
        using var response = await factory.GetJob("throwing");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(await factory.TryAdvisoryLock("throwing"));
    }

    [Fact]
    public async Task Failed_job_records_the_error_type_only()
    {
        using var response = await factory.GetJob("throwing");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var row = await factory.JobRun("throwing");
        Assert.Equal(nameof(InvalidOperationException), row.ErrorType);
        Assert.DoesNotContain("secret exception", row.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Job_past_its_budget_ends_partial_and_the_next_call_resumes()
    {
        factory.BudgetSeconds = 0;
        using (var first = await factory.GetJob("steps"))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal("partial", (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        }
        using (var second = await factory.GetJob("steps"))
            Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(new[] { "<null>", "1" }, factory.StepsJob.Cursors);
        Assert.Equal("Succeeded", (await factory.JobRun("steps")).Status);
    }

    [Fact]
    public async Task Done_job_clears_its_checkpoint()
    {
        factory.BudgetSeconds = 0;
        using var first = await factory.GetJob("steps");
        factory.BudgetSeconds = 50;
        using var second = await factory.GetJob("steps");
        Assert.Equal(0, await factory.Scalar<long>("select count(*) from fleetops.job_checkpoints where name = 'steps'"));
    }

    [Fact]
    public async Task Failed_job_keeps_its_last_checkpoint()
    {
        using var response = await factory.GetJob("checkpoint-failure");
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("saved", await factory.Scalar<string>("select cursor from fleetops.job_checkpoints where name = 'checkpoint-failure'"));
    }

    [Fact]
    public async Task Nightly_runs_each_job_with_its_own_run_row()
    {
        factory.NightlyOrder = new[] { "nightly-a", "nightly-b" };
        using var response = await factory.GetNightly();
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await factory.JobRunCount("nightly-a"));
        Assert.Equal(1, await factory.JobRunCount("nightly-b"));
    }

    [Fact]
    public async Task Admin_trigger_runs_for_the_owner_in_demo()
    {
        factory.AdminTrigger = true;
        using var response = await factory.RunAdmin("recording", RoleCode.Owner);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        Assert.Equal("admin", (await factory.JobRun("recording")).Trigger);
    }

    [Fact]
    public async Task Admin_trigger_is_refused_for_a_dispatcher()
    {
        factory.AdminTrigger = true;
        using var response = await factory.RunAdmin("recording", RoleCode.Dispatcher);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_trigger_is_404_when_off()
    {
        using var run = await factory.RunAdmin("recording", RoleCode.Owner);
        using var list = await factory.GetAdminJobs(RoleCode.Owner);
        Assert.True(run.StatusCode == HttpStatusCode.NotFound, await run.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    }
}

public sealed class JobApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly JobAccessAccessor access = new();
    private TestPostgres? postgres;
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.Parse("2026-11-06T10:00:00Z"));
    public string Secret { get; set; } = "test-cron-secret";
    public int BudgetSeconds { get; set; } = 50;
    public bool AdminTrigger { get; set; }
    public bool IncludeOrphanJob { get; set; }
    public string[] NightlyOrder { get; set; } = [];
    internal RecordingJob RecordingJob { get; } = new();
    internal BlockingJob BlockingJob { get; private set; } = null!;
    internal StepsJob StepsJob { get; } = new();
    public int RecordingRuns => RecordingJob.Runs;
    public HttpClient Client => CreateClient();
    public string RuntimeConnectionString => postgres!.ConnectionString;
    public string AdminConnectionString => postgres!.AdminConnectionString;

    public async ValueTask InitializeAsync()
    {
        postgres = await TestPostgres.StartAsync(nameof(JobApiFactory));
        BlockingJob = new BlockingJob(RuntimeConnectionString);
    }
    public new async ValueTask DisposeAsync() { await base.DisposeAsync(); if (postgres is not null) await postgres.DisposeAsync(); }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:FleetOps", RuntimeConnectionString);
        builder.UseSetting("Auth:PublicOrigin", "https://localhost");
        builder.UseSetting("Jobs:BudgetSeconds", BudgetSeconds.ToString());
        builder.UseSetting("Jobs:AdminTrigger", AdminTrigger.ToString());
        builder.UseSetting("Jobs:NightlyOrder:0", NightlyOrder.ElementAtOrDefault(0));
        builder.UseSetting("Jobs:NightlyOrder:1", NightlyOrder.ElementAtOrDefault(1));
        if (Secret is not null) builder.UseSetting("Jobs:Secret", Secret);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccessContextAccessor>();
            services.AddSingleton<IAccessContextAccessor>(_ => access);
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.RemoveAll<IJob>();
            services.AddScoped<IJob>(_ => RecordingJob);
            services.AddScoped<IJob>(_ => BlockingJob);
            services.AddScoped<IJob>(_ => new ThrowingJob());
            services.AddScoped<IJob>(_ => StepsJob);
            services.AddScoped<IJob>(_ => new CheckpointFailureJob());
            services.AddScoped<IJob>(_ => new SimpleJob("nightly-a"));
            services.AddScoped<IJob>(_ => new SimpleJob("nightly-b"));
            if (IncludeOrphanJob) services.AddScoped<IJob, FileOrphansJob>();
            services.RemoveAll<IObjectStore>();
            services.AddSingleton<FakeObjectStore>();
            services.AddSingleton<IObjectStore>(services => services.GetRequiredService<FakeObjectStore>());
        });
    }

    public async Task<HttpResponseMessage> GetJob(string name, string? secret = "test-cron-secret", bool signedIn = false)
    {
        access.Current = signedIn ? AccessContext.For(VehicleApiFactory.ActorId, RoleCode.Owner) : null;
        var request = new HttpRequestMessage(HttpMethod.Get, $"/jobs/{name}");
        if (secret is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        return await Client.SendAsync(request);
    }
    public Task<HttpResponseMessage> GetNightly() => GetJob("nightly");
    public Task<HttpResponseMessage> RunAdmin(string name, RoleCode role) { access.Current = AccessContext.For(VehicleApiFactory.ActorId, role); var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/admin/jobs/{name}/run"); request.Headers.TryAddWithoutValidation("Origin", "https://localhost"); request.Headers.TryAddWithoutValidation("X-FleetOps-Request", "1"); return Client.SendAsync(request); }
    public Task<HttpResponseMessage> GetAdminJobs(RoleCode role) { access.Current = AccessContext.For(VehicleApiFactory.ActorId, role); var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/admin/jobs"); request.Headers.TryAddWithoutValidation("Origin", "https://localhost"); request.Headers.TryAddWithoutValidation("X-FleetOps-Request", "1"); return Client.SendAsync(request); }
    public async Task<NpgsqlConnection> DirectConnection() { var connection = new NpgsqlConnection(RuntimeConnectionString); await connection.OpenAsync(); return connection; }
    public async Task<T> Scalar<T>(string sql) { await using var connection = await DirectConnection(); await using var command = new NpgsqlCommand(sql, connection); return (T)(await command.ExecuteScalarAsync())!; }
    public Task<bool> TryAdvisoryLock(string name) => TryAdvisoryLockAsync(name);
    private async Task<bool> TryAdvisoryLockAsync(string name) { await using var connection = await DirectConnection(); await using var command = new NpgsqlCommand("select pg_try_advisory_lock(hashtextextended('fleetops.job.' || $1, 0))", connection); command.Parameters.AddWithValue(name); return (bool)(await command.ExecuteScalarAsync())!; }
    public Task<int> JobRunCount(string name) => Scalar<int>($"select count(*)::int from fleetops.job_runs where name = '{name}'");
    public async Task<JobRunRow> JobRun(string name) { await using var connection = await DirectConnection(); await using var command = new NpgsqlCommand("select status, actor_name, trigger, finished_at, error_type, coalesce(error_type, '') from fleetops.job_runs where name = $1 order by started_at desc limit 1", connection); command.Parameters.AddWithValue(name); await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync()); return new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)); }
}

public sealed record JobRunRow(string Status, string ActorName, string Trigger, DateTimeOffset? FinishedAt, string? ErrorType, string AllText);
internal sealed class JobAccessAccessor : IAccessContextAccessor { public AccessContext? Current { get; set; } }
internal sealed class RecordingJob : IJob { public string Name => "recording"; public int Runs; public Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken) { Interlocked.Increment(ref Runs); return Task.FromResult(new JobStep(true, null, 1)); } }
internal sealed class BlockingJob(string connectionString) : IJob { public string Name => "blocking"; public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously); public TaskCompletionSource? Release { get; set; } public long DataConnectionPid { get; private set; } public async Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken) { await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("select pg_backend_pid()", connection); DataConnectionPid = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)); Started.SetResult(); if (Release is not null) await Release.Task.WaitAsync(cancellationToken); return new(true, null, 1); } }
internal sealed class ThrowingJob : IJob { public string Name => "throwing"; public Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("secret exception"); }
internal sealed class StepsJob : IJob { public string Name => "steps"; public ConcurrentQueue<string> Cursors { get; } = new(); public Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken) { Cursors.Enqueue(context.Cursor ?? "<null>"); return Task.FromResult<JobStep>(context.Cursor is null ? new JobStep(false, "1", 1) : new JobStep(true, null, 1)); } }
internal sealed class CheckpointFailureJob : IJob { public string Name => "checkpoint-failure"; public async Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken) { if (context.Cursor is null) return new JobStep(false, "saved", 1); throw new InvalidOperationException("checkpoint failure"); } }
internal sealed class SimpleJob(string name) : IJob { public string Name => name; public Task<JobStep> RunStepAsync(JobContext context, CancellationToken cancellationToken) => Task.FromResult(new JobStep(true, null, 1)); }
