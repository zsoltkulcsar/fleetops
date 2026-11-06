using System.Net;
using System.Net.Http.Json;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
using FleetOps.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

[CollectionDefinition("Demo seed", DisableParallelization = true)]
public sealed class DemoSeedCollection : ICollectionFixture<object>;

[Collection("Demo seed")]
public sealed class SeedTests
{
    private const string Password = "demo-password-for-tests";

    [Fact]
    public async Task Seed_creates_the_fake_company()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        await SeedAsync(postgres);
        var counts = await CountsAsync(postgres);
        Assert.Equal(28, counts.Employees);
        Assert.Equal(4, counts.Users);
        Assert.Equal(3, counts.Roles);
        Assert.Equal(4, counts.UserRoles);
        Assert.Equal(35, counts.Vehicles);
        Assert.Equal(0, counts.AuditEntries);
        Assert.Equal(1, counts.Depots);
    }

    [Fact]
    public async Task Seed_rerun_restores_the_same_state()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        await SeedAsync(postgres);
        var before = await SnapshotAsync(postgres);
        await using (var connection = new NpgsqlConnection(postgres.MigratorConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("update fleetops.employees set last_name = 'Changed' where email = 'driver1@demo.fleetops.example'; delete from fleetops.vehicles where plate = 'B-D 0001';", connection);
            await command.ExecuteNonQueryAsync();
        }

        await SeedAsync(postgres);
        Assert.Equal(before, await SnapshotAsync(postgres));
    }

    [Fact]
    public async Task Seed_refuses_production_and_writes_nothing()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        await SeedAsync(postgres);
        var before = await CountsAsync(postgres);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(postgres, "Production", Password));
        Assert.Equal(before, await CountsAsync(postgres));
    }

    [Fact]
    public async Task Seed_refuses_an_unknown_or_empty_environment()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        foreach (var environment in new[] { "", "Unknown" })
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(postgres, environment, Password));
            Assert.Contains("FLEETOPS_ENVIRONMENT", exception.Message);
        }
        Assert.Equal(0, (await CountsAsync(postgres)).Employees);
    }

    [Fact]
    public async Task Seed_refuses_a_missing_or_short_password_and_writes_nothing()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        foreach (var password in new string?[] { null, "short" })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(postgres, "Demo", password));
        }
        Assert.Equal(0, (await CountsAsync(postgres)).Employees);
    }

    [Fact]
    public async Task Seed_failure_rolls_back_everything()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        await SeedAsync(postgres);
        var before = await SnapshotAsync(postgres);
        await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(postgres, "Demo", Password, _ => new InvalidOperationException("forced seed failure")));
        Assert.Equal(before, await SnapshotAsync(postgres));
    }

    [Fact]
    public async Task Seeded_dispatcher_signs_in_through_the_real_api_and_reaches_the_privacy_notice()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        await SeedAsync(postgres);
        await using var api = new SeedApiFactory(postgres.ConnectionString);
        using var client = api.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://localhost");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-FleetOps-Request", "1");
        using var response = await client.PostAsJsonAsync("/api/v1/auth/sign-in", new { email = "dispatcher1@demo.fleetops.example", password = Password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Equal("privacy_notice", body!["next"]);
    }

    [Fact]
    public async Task Seeded_vehicles_pass_the_real_validators()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        await SeedAsync(postgres);
        await using var connection = new NpgsqlConnection(postgres.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select kind, plate, vin, make, model, year, gross_weight_kg, ownership, lease_end_date, trailer_type, odometer_km from fleetops.vehicles order by id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var kind = Enum.Parse<VehicleKind>(reader.GetString(0));
            var ownership = Enum.Parse<VehicleOwnership>(reader.GetString(7));
            var trailerType = reader.IsDBNull(9) ? (TrailerType?)null : Enum.Parse<TrailerType>(reader.GetString(9));
            var fields = VehicleRules.Validate(kind, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6), ownership, reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8), trailerType, reader.IsDBNull(10) ? null : reader.GetInt32(10), "seed verification");
            Assert.Empty(fields);
        }
    }

    [Fact]
    public async Task Seed_uses_only_reserved_example_addresses()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        await SeedAsync(postgres);
        await using var connection = new NpgsqlConnection(postgres.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select email from fleetops.employees", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            Assert.EndsWith("@demo.fleetops.example", reader.GetString(0));
        }
    }

    [Fact]
    public async Task Seed_output_never_contains_the_password()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(SeedTests));
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(postgres, "Production", Password));
        Assert.DoesNotContain(Password, exception.Message);
    }

    private static async Task SeedAsync(TestPostgres postgres) => await RunAsync(postgres, "Demo", Password);

    private static async Task RunAsync(TestPostgres postgres, string? environment, string? password, Func<int, Exception?>? failureFactory = null)
    {
        var previousEnvironment = Environment.GetEnvironmentVariable("FLEETOPS_ENVIRONMENT");
        var previousPassword = Environment.GetEnvironmentVariable("FLEETOPS_DEMO_PASSWORD");
        var previousConnection = Environment.GetEnvironmentVariable("FLEETOPS_DATABASE_MIGRATION_URL");
        try
        {
            Environment.SetEnvironmentVariable("FLEETOPS_ENVIRONMENT", environment);
            Environment.SetEnvironmentVariable("FLEETOPS_DEMO_PASSWORD", password);
            Environment.SetEnvironmentVariable("FLEETOPS_DATABASE_MIGRATION_URL", postgres.MigratorConnectionString);
            await new DemoSeeder(failureFactory).RunAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable("FLEETOPS_ENVIRONMENT", previousEnvironment);
            Environment.SetEnvironmentVariable("FLEETOPS_DEMO_PASSWORD", previousPassword);
            Environment.SetEnvironmentVariable("FLEETOPS_DATABASE_MIGRATION_URL", previousConnection);
        }
    }

    private static async Task<Counts> CountsAsync(TestPostgres postgres)
    {
        await using var connection = new NpgsqlConnection(postgres.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select (select count(*) from fleetops.employees), (select count(*) from fleetops.users), (select count(*) from fleetops.roles), (select count(*) from fleetops.user_roles), (select count(*) from fleetops.vehicles), (select count(*) from fleetops.audit_entries), (select count(*) from fleetops.depots)", connection);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new Counts(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6));
    }

    private static async Task<string> SnapshotAsync(TestPostgres postgres)
    {
        await using var connection = new NpgsqlConnection(postgres.MigratorConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select coalesce(string_agg(id::text || '|' || coalesce(email, '') || '|' || coalesce(first_name, '') || '|' || coalesce(last_name, '') || '|' || coalesce(plate, '') || '|' || coalesce(vin, ''), ',' order by id), '') from (select id, email, first_name, last_name, null::text plate, null::text vin from fleetops.employees union all select id, null, null, null, plate, vin from fleetops.vehicles) rows", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private sealed record Counts(long Employees, long Users, long Roles, long UserRoles, long Vehicles, long AuditEntries, long Depots);
}

internal sealed class SeedApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:FleetOps", connectionString);
        builder.UseSetting("Auth:PublicOrigin", "https://localhost");
        builder.ConfigureTestServices(_ => { });
    }
}
