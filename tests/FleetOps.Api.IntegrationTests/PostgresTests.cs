using Npgsql;
using FleetOps.Migrations;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace FleetOps.Tests;

public sealed class PostgresTests
{
    [Fact]
    public async Task Postgres_17_accepts_a_tls_capable_database_connection()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand("select 1", connection);
        Assert.Equal(1, await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Runtime_role_can_read_the_marker_but_cannot_execute_schema_ddl()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:17").Build();
        await postgres.StartAsync(TestContext.Current.CancellationToken);
        var admin = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString());
        await using (var adminConnection = new NpgsqlConnection(admin.ConnectionString))
        {
            await adminConnection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = new NpgsqlCommand(
                "CREATE ROLE fleetops_migrator LOGIN PASSWORD 'migrator-password'; " +
                "CREATE ROLE fleetops_runtime LOGIN PASSWORD 'runtime-password'; " +
                "CREATE SCHEMA fleetops AUTHORIZATION fleetops_migrator;", adminConnection);
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var migrator = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        { Username = "fleetops_migrator", Password = "migrator-password" };
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, migrator.ConnectionString);
        await using (var db = new FleetOpsDbContext(options.Options))
            await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
        var runtime = new NpgsqlConnectionStringBuilder(admin.ConnectionString)
        { Username = "fleetops_runtime", Password = "runtime-password" };
        await using var runtimeConnection = new NpgsqlConnection(runtime.ConnectionString);
        await runtimeConnection.OpenAsync(TestContext.Current.CancellationToken);
        await using (var ddl = new NpgsqlCommand(
            "CREATE TABLE fleetops.runtime_role_must_not_ddl (id integer);", runtimeConnection))
        {
            var exception = await Assert.ThrowsAsync<PostgresException>(() =>
                ddl.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
            Assert.Equal("42501", exception.SqlState);
        }
        await using var select = new NpgsqlCommand(
            "SELECT name FROM fleetops.migration_marker WHERE id = 1;", runtimeConnection);
        Assert.Equal("solution-skeleton", await select.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }
}
