using Npgsql;
using Testcontainers.PostgreSql;

namespace FleetOps.Tests;

internal static class TestPostgres
{
    public static bool UseAdminServer => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FLEETOPS_TEST_ADMIN_URL"));

    public static string CreateConnectionString(string owner)
    {
        var admin = Environment.GetEnvironmentVariable("FLEETOPS_TEST_ADMIN_URL");
        if (string.IsNullOrWhiteSpace(admin)) throw new InvalidOperationException("The admin server is not configured.");
        var database = $"fleetops_{Sanitize(owner)[..Math.Min(12, Sanitize(owner).Length)]}_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(admin) { Database = "postgres" };
        using var connection = new NpgsqlConnection(builder.ConnectionString);
        connection.Open();
        using (var create = new NpgsqlCommand($"CREATE DATABASE {Quote(database)}", connection)) create.ExecuteNonQuery();
        return new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
    }

    public static async Task EnsureRolesAndSchemaAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'fleetops_migrator') THEN CREATE ROLE fleetops_migrator LOGIN PASSWORD 'migrator-password'; END IF; " +
            "IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'fleetops_runtime') THEN CREATE ROLE fleetops_runtime LOGIN PASSWORD 'runtime-password'; END IF; END $$; " +
            "CREATE SCHEMA IF NOT EXISTS fleetops AUTHORIZATION fleetops_migrator;", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static string ContainerConnectionString(PostgreSqlContainer postgres) => postgres.GetConnectionString();

    public static async Task<(PostgreSqlContainer? Container, string ConnectionString)> StartAsync(string owner, CancellationToken cancellationToken)
    {
        if (UseAdminServer)
        {
            var connectionString = CreateConnectionString(owner);
            await EnsureRolesAndSchemaAsync(connectionString, cancellationToken);
            return (null, connectionString);
        }
        var container = new PostgreSqlBuilder("postgres:17").Build();
        await container.StartAsync(cancellationToken);
        return (container, container.GetConnectionString());
    }

    private static string Sanitize(string value) => new(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
