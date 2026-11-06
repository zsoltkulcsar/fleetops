using Npgsql;
using Testcontainers.PostgreSql;
using System.Collections.Concurrent;
using FleetOps.Migrations;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Tests;

internal sealed class TestPostgres : IAsyncDisposable
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<TestPostgres>>> Instances = new();
    private readonly PostgreSqlContainer? _container;
    private string _connectionString = "";

    private TestPostgres(PostgreSqlContainer container) => _container = container;
    private TestPostgres(string connectionString) => _connectionString = connectionString;

    public string ConnectionString => _connectionString;

    public static async Task<TestPostgres> StartAsync(string testClass, CancellationToken cancellationToken = default)
    {
        var instance = Instances.GetOrAdd(testClass, key => new Lazy<Task<TestPostgres>>(() => StartCoreAsync(key, cancellationToken))).Value;
        return await instance;
    }

    private static async Task<TestPostgres> StartCoreAsync(string testClass, CancellationToken cancellationToken)
    {
        var adminUrl = Environment.GetEnvironmentVariable("FLEETOPS_TEST_ADMIN_URL");
        if (string.IsNullOrWhiteSpace(adminUrl))
        {
            var container = new PostgreSqlBuilder("postgres:17").Build();
            await container.StartAsync(cancellationToken);
            var connectionString = container.GetConnectionString();
            await SetupDatabaseAsync(connectionString, cancellationToken);
            return new TestPostgres(container) { _connectionString = connectionString };
        }

        var admin = new NpgsqlConnectionStringBuilder(adminUrl);
        var database = $"fleetops_test_{Sanitize(testClass)}_{Environment.ProcessId}";
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var createDatabase = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await createDatabase.ExecuteNonQueryAsync(cancellationToken);
        }

        admin.Database = database;
        await SetupDatabaseAsync(admin.ConnectionString, cancellationToken);
        return new TestPostgres(admin.ConnectionString);
    }

    private static async Task SetupDatabaseAsync(string adminConnectionString, CancellationToken cancellationToken)
    {
        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var roles = new NpgsqlCommand(
                "SELECT pg_advisory_lock(71414); " +
                "DO $$ BEGIN " +
                "IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fleetops_migrator') THEN " +
                "CREATE ROLE fleetops_migrator LOGIN PASSWORD 'migrator-password'; END IF; " +
                "IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fleetops_runtime') THEN " +
                "CREATE ROLE fleetops_runtime LOGIN PASSWORD 'runtime-password'; END IF; " +
                "END $$; " +
                "SELECT pg_advisory_unlock(71414);",
                connection);
            await roles.ExecuteNonQueryAsync(cancellationToken);

            await using var schema = new NpgsqlCommand(
                "CREATE SCHEMA IF NOT EXISTS fleetops AUTHORIZATION fleetops_migrator;",
                connection);
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        var migrator = new NpgsqlConnectionStringBuilder(adminConnectionString)
        {
            Username = "fleetops_migrator",
            Password = "migrator-password"
        };
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, migrator.ConnectionString);
        await using var db = new FleetOpsDbContext(options.Options);
        await db.Database.MigrateAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static string Sanitize(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
