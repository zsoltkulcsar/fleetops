using System.Collections.Concurrent;
using FleetOps.Migrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FleetOps.Tests;

internal sealed class TestPostgres : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<TestPostgres>>> Instances = new();
    private readonly PostgreSqlContainer? container;
    private TestPostgres(PostgreSqlContainer? container, string adminConnectionString) { this.container = container; AdminConnectionString = adminConnectionString; ConnectionString = RuntimeConnection(adminConnectionString); }
    public string AdminConnectionString { get; }
    public string ConnectionString { get; }
    public static bool UseAdminServer => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLEETOPS_TEST_ADMIN_URL"));

    public static Task<TestPostgres> StartAsync(string owner, CancellationToken cancellationToken = default) => Instances.GetOrAdd(owner, key => new Lazy<Task<TestPostgres>>(() => StartCoreAsync(key, cancellationToken))).Value;

    private static async Task<TestPostgres> StartCoreAsync(string owner, CancellationToken cancellationToken)
    {
        PostgreSqlContainer? container = null;
        string adminConnectionString;
        var adminUrl = Environment.GetEnvironmentVariable("FLEETOPS_TEST_ADMIN_URL");
        if (string.IsNullOrWhiteSpace(adminUrl))
        {
            container = new PostgreSqlBuilder("postgres:17").Build();
            await container.StartAsync(cancellationToken);
            adminConnectionString = container.GetConnectionString();
        }
        else
        {
            var admin = new NpgsqlConnectionStringBuilder(adminUrl) { Database = "postgres" };
            var database = $"fleetops_test_{Sanitize(owner)}_{Environment.ProcessId}_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(admin.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {Quote(database)}", connection);
            await create.ExecuteNonQueryAsync(cancellationToken);
            admin.Database = database;
            adminConnectionString = admin.ConnectionString;
        }
        await SetupDatabaseAsync(adminConnectionString, cancellationToken);
        return new TestPostgres(container, adminConnectionString);
    }

    private static async Task SetupDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken)
    {
        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var roles = new NpgsqlCommand("SELECT pg_advisory_lock(71414); DO $$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fleetops_migrator') THEN CREATE ROLE fleetops_migrator LOGIN PASSWORD 'migrator-password'; END IF; IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fleetops_runtime') THEN CREATE ROLE fleetops_runtime LOGIN PASSWORD 'runtime-password'; END IF; END $$; SELECT pg_advisory_unlock(71414);", connection);
            await roles.ExecuteNonQueryAsync(cancellationToken);
            await using var schema = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS fleetops AUTHORIZATION fleetops_migrator;", connection);
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }
        var migrator = new NpgsqlConnectionStringBuilder(adminConnectionString) { Username = "fleetops_migrator", Password = "migrator-password" };
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, migrator.ConnectionString);
        await using var db = new FleetOpsDbContext(options.Options);
        await db.Database.MigrateAsync(cancellationToken);
    }

    private static string RuntimeConnection(string admin) => new NpgsqlConnectionStringBuilder(admin) { Username = "fleetops_runtime", Password = "runtime-password" }.ConnectionString;
    private static string Sanitize(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    public async ValueTask DisposeAsync() { if (container is not null) await container.DisposeAsync(); }
}
