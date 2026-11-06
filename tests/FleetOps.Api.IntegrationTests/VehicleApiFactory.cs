using System.Net.Http.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

/// <summary>
/// Hosts the API against a fresh migrated database. The host connects as fleetops_runtime;
/// the schema comes only from migrations run as fleetops_migrator (see <see cref="TestPostgres"/>).
/// The signed-in actor is set per test through <see cref="AsDispatcher"/> and its siblings.
/// </summary>
public sealed class VehicleApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly Guid ActorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private static readonly HashSet<string> CountableTables =
    [
        "vehicles", "audit_entries", "vehicle_plate_history", "depots",
    ];

    private static int sequence;

    private readonly MutableAccessor accessor = new();
    private TestPostgres? postgres;

    /// <summary>When set, the next write command runs inside its transaction and then throws.</summary>
    public bool ThrowAfterWrite { get; set; }

    public string RuntimeConnectionString => postgres?.ConnectionString
        ?? throw new InvalidOperationException("Database is not initialized.");

    /// <summary>Owner-level connection, for test-only fixture tables that are not part of the migrations.</summary>
    public string AdminConnectionString => postgres?.AdminConnectionString
        ?? throw new InvalidOperationException("Database is not initialized.");

    public async ValueTask InitializeAsync()
    {
        postgres = await TestPostgres.StartAsync(nameof(VehicleApiFactory), TestContext.Current.CancellationToken);
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        if (postgres is not null)
        {
            await postgres.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:FleetOps", RuntimeConnectionString);
        builder.UseSetting("Auth:PublicOrigin", FleetRequestHeaders.Origin);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAccessContextAccessor>(accessor);
            services.AddScoped<IVehicleStore>(_ => ThrowAfterWrite
                ? new FailingVehicleStore(new EfVehicleStore(), () => ThrowAfterWrite = false)
                : new EfVehicleStore());
        });
    }

    public HttpClient AsDispatcher() => ClientAs(RoleCode.Dispatcher);

    public HttpClient AsOwner() => ClientAs(RoleCode.Owner);

    public HttpClient AsHr() => ClientAs(RoleCode.Hr);

    public HttpClient AsDriver() => ClientAs(RoleCode.Driver);

    /// <summary>A client that sends the origin headers but, until a test calls AsX, has no signed-in actor.</summary>
    public HttpClient Anonymous() => ClientWithHeaders();

    /// <summary>A plate that no other call in this process has used.</summary>
    public string NextPlate() => $"B-X {Interlocked.Increment(ref sequence):0000}";

    /// <summary>A valid VIN that no other call in this process has used.</summary>
    public string NextVin() => $"WVWZZZ1JZXW{Interlocked.Increment(ref sequence):000000}";

    public async Task<VehicleDetail> CreateTruck(
        HttpClient client,
        string? plate = null,
        string? vin = null,
        string reason = "create reason",
        int grossWeightKg = 8000)
    {
        var number = Interlocked.Increment(ref sequence);
        plate ??= $"B-T {number:0000}";
        vin ??= $"WVWZZZ1JZXW{number:000000}";
        using var response = await client.PostAsJsonAsync(
            "/api/v1/fleet/vehicles",
            new
            {
                kind = 0,
                plate,
                vin,
                make = "MAN",
                model = "TGX",
                year = 2020,
                grossWeightKg,
                requiredLicenceClass = "C",
                ownership = 0,
                leaseEndDate = (DateOnly?)null,
                trailerType = (int?)null,
                odometerKm = 1000,
                depotId = (Guid?)null,
                serviceIntervalKm = 50000,
                serviceIntervalMonths = 12,
                lastTachoDownloadAt = (DateOnly?)null,
                reason,
            },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetail>(cancellationToken: TestContext.Current.CancellationToken))!;
    }

    public async Task<long> CountRows(string table)
    {
        if (!CountableTables.Contains(table))
        {
            throw new ArgumentException($"Table {table} is not countable.", nameof(table));
        }

        await using var connection = await DirectConnection();
        await using var command = new NpgsqlCommand($"select count(*) from fleetops.{table}", connection);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>An independent connection as fleetops_runtime, for reading what the API wrote.</summary>
    public async Task<NpgsqlConnection> DirectConnection()
    {
        var connection = new NpgsqlConnection(RuntimeConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    private HttpClient ClientAs(RoleCode role)
    {
        accessor.Current = AccessContext.For(ActorId, role);
        return ClientWithHeaders();
    }

    private HttpClient ClientWithHeaders()
    {
        var client = CreateClient();
        FleetRequestHeaders.Apply(client);
        return client;
    }

    private sealed class MutableAccessor : IAccessContextAccessor
    {
        public AccessContext? Current { get; set; }
    }

    private sealed class FailingVehicleStore(EfVehicleStore inner, Action clearFailure) : IVehicleStore
    {
        public async Task<VehicleDetail> CreateAsync(
            CreateVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        {
            _ = await inner.CreateAsync(command, actor, context, cancellationToken);
            clearFailure();
            throw new InvalidOperationException("after write");
        }

        public async Task<VehicleDetail> EditAsync(
            EditVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        {
            _ = await inner.EditAsync(command, actor, context, cancellationToken);
            clearFailure();
            throw new InvalidOperationException("after write");
        }
    }
}
