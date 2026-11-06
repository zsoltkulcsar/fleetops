using FleetOps.Migrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace FleetOps.Tests;

/// <summary>
/// One PostgreSQL server per test process (a Testcontainers container, or the server named by
/// FLEETOPS_TEST_ADMIN_URL), and one fresh, fully migrated database per <see cref="StartAsync"/> call.
/// Callers own their database and may dispose it without affecting anybody else.
/// The container itself is never disposed by a test; Testcontainers removes it when the process ends.
/// </summary>
internal sealed class TestPostgres : IAsyncDisposable
{
    private const string MigratorPassword = "migrator-password";
    private const string RuntimePassword = "runtime-password";
    private static readonly Lazy<Task<string>> ServerAdminConnection = new(StartServerAsync);

    private readonly string serverAdminConnection;
    private readonly string databaseName;
    private bool disposed;

    private TestPostgres(string serverAdminConnection, string databaseName)
    {
        this.serverAdminConnection = serverAdminConnection;
        this.databaseName = databaseName;
        AdminConnectionString = new NpgsqlConnectionStringBuilder(serverAdminConnection) { Database = databaseName }.ConnectionString;
        MigratorConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = "fleetops_migrator",
            Password = MigratorPassword,
        }.ConnectionString;
        ConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = "fleetops_runtime",
            Password = RuntimePassword,
        }.ConnectionString;
    }

    public string AdminConnectionString { get; }

    public string MigratorConnectionString { get; }

    /// <summary>Connection string of the role the application runs as (fleetops_runtime).</summary>
    public string ConnectionString { get; }

    public static bool UseAdminServer => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLEETOPS_TEST_ADMIN_URL"));

    public static async Task<TestPostgres> StartAsync(string owner, CancellationToken cancellationToken = default)
    {
        var server = await ServerAdminConnection.Value;
        var databaseName = $"fleetops_test_{Sanitize(owner)}_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(server))
        {
            await connection.OpenAsync(cancellationToken);
            await using var create = new NpgsqlCommand($"CREATE DATABASE {Quote(databaseName)}", connection);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var postgres = new TestPostgres(server, databaseName);
        await postgres.MigrateAsync(cancellationToken);
        return postgres;
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            NpgsqlConnection.ClearAllPools();
            await using var connection = new NpgsqlConnection(serverAdminConnection);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {Quote(databaseName)} WITH (FORCE)", connection);
            await drop.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // Best effort: a leftover scratch database must never fail a test.
        }
    }

    private static async Task<string> StartServerAsync()
    {
        string adminConnection;
        var adminUrl = Environment.GetEnvironmentVariable("FLEETOPS_TEST_ADMIN_URL");
        if (string.IsNullOrWhiteSpace(adminUrl))
        {
            var container = new PostgreSqlBuilder("postgres:17").Build();
            await container.StartAsync();
            adminConnection = container.GetConnectionString();
        }
        else
        {
            adminConnection = new NpgsqlConnectionStringBuilder(adminUrl) { Database = "postgres" }.ConnectionString;
        }

        await EnsureRolesAsync(adminConnection);
        return adminConnection;
    }

    private static async Task EnsureRolesAsync(string adminConnection)
    {
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var gate = new NpgsqlCommand("select pg_advisory_xact_lock(71414)", connection, transaction))
        {
            await gate.ExecuteNonQueryAsync();
        }

        const string createRoles = $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fleetops_migrator') THEN
                    CREATE ROLE fleetops_migrator LOGIN PASSWORD '{MigratorPassword}';
                END IF;
                IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'fleetops_runtime') THEN
                    CREATE ROLE fleetops_runtime LOGIN PASSWORD '{RuntimePassword}';
                END IF;
            END $$;
            """;
        await using (var roles = new NpgsqlCommand(createRoles, connection, transaction))
        {
            await roles.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using (var connection = new NpgsqlConnection(AdminConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var schema = new NpgsqlCommand("CREATE SCHEMA IF NOT EXISTS fleetops AUTHORIZATION fleetops_migrator", connection);
            await schema.ExecuteNonQueryAsync(cancellationToken);
        }

        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, MigratorConnectionString);
        await using var db = new FleetOpsDbContext(options.Options);
        await db.Database.MigrateAsync(cancellationToken);
    }

    private static string Sanitize(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
