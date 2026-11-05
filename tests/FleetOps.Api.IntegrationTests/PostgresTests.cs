using Npgsql;
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
}
