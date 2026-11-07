using System.Net;
using FleetOps.Kernel.Files;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class FileOrphanJobTests : IAsyncLifetime
{
    private readonly JobApiFactory factory = new() { IncludeOrphanJob = true };

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Orphan_job_removes_an_expired_unconfirmed_upload()
    {
        var key = await AddPendingFile(DateTimeOffset.Parse("2026-11-06T08:00:00Z"));
        Store.Put(key, 10, "application/pdf");
        using var response = await Run();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(key, Store.Deleted);
        Assert.Equal(0, await PendingCount(key));
    }

    [Fact]
    public async Task Orphan_job_keeps_a_fresh_upload()
    {
        var key = await AddPendingFile(DateTimeOffset.Parse("2026-11-06T09:30:00Z"));
        Store.Put(key, 10, "application/pdf");
        using var response = await Run();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(key, Store.Deleted);
        Assert.Equal(1, await PendingCount(key));
    }

    [Fact]
    public async Task Orphan_job_keeps_a_confirmed_file()
    {
        var key = await AddPendingFile(DateTimeOffset.Parse("2026-11-06T08:00:00Z"), DateTimeOffset.Parse("2026-11-06T08:30:00Z"));
        Store.Put(key, 10, "application/pdf");
        using var response = await Run();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(key, Store.Deleted);
        Assert.Equal(1, await PendingCount(key));
    }

    [Fact]
    public async Task Orphan_job_ignores_an_object_already_gone()
    {
        var key = await AddPendingFile(DateTimeOffset.Parse("2026-11-06T08:00:00Z"));
        using var response = await Run();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(key, Store.Deleted);
        Assert.Equal(0, await PendingCount(key));
    }

    [Fact]
    public async Task Orphan_job_can_run_twice_with_no_change()
    {
        var key = await AddPendingFile(DateTimeOffset.Parse("2026-11-06T08:00:00Z"));
        Store.Put(key, 10, "application/pdf");
        using var first = await Run();
        using var second = await Run();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(0, await PendingCount(key));
        Assert.Single(Store.Deleted, deletedKey => deletedKey == key);
    }

    private FakeObjectStore Store => factory.Services.GetRequiredService<FakeObjectStore>();

    private async Task<HttpResponseMessage> Run() => await factory.GetJob("file-orphans");

    private async Task<string> AddPendingFile(DateTimeOffset expiresAt, DateTimeOffset? confirmedAt = null)
    {
        var key = $"documents/{Guid.NewGuid():N}";
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.pending_files (id, object_key, actor_id, purpose, content_type, max_size_bytes, created_at, expires_at, confirmed_at) values ($1, $2, $3, 'document', 'application/pdf', 100, $4, $5, $6)", connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(key);
        command.Parameters.AddWithValue(VehicleApiFactory.ActorId);
        command.Parameters.AddWithValue(DateTimeOffset.Parse("2026-11-06T07:00:00Z"));
        command.Parameters.AddWithValue(expiresAt);
        command.Parameters.AddWithValue(confirmedAt is null ? DBNull.Value : confirmedAt.Value);
        await command.ExecuteNonQueryAsync();
        return key;
    }

    private async Task<int> PendingCount(string key)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("select count(*)::int from fleetops.pending_files where object_key = $1", connection);
        command.Parameters.AddWithValue(key);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
