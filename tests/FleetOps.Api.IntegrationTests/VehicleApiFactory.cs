using System.Net.Http.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class VehicleApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private TestPostgres? postgres;
    private readonly MutableAccessor accessor = new();
    private int sequence;
    public bool ThrowAfterWrite { get; set; }
    public string AdminConnectionString => postgres?.AdminConnectionString ?? throw new InvalidOperationException("Database is not initialized.");

    public async ValueTask InitializeAsync()
    {
        postgres = await TestPostgres.StartAsync(nameof(VehicleApiFactory), TestContext.Current.CancellationToken);
    }

    public new async ValueTask DisposeAsync() { if (postgres is not null) await postgres.DisposeAsync(); }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:FleetOps", postgres?.ConnectionString ?? throw new InvalidOperationException("Database is not initialized."));
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAccessContextAccessor>(accessor);
            services.AddScoped<IVehicleStore>(sp => ThrowAfterWrite ? new FailingVehicleStore(new EfVehicleStore()) : new EfVehicleStore());
        });
    }

    public HttpClient AsDispatcher() { SetActor(RoleCode.Dispatcher); return CreateClient(); }
    public HttpClient AsOwner() { SetActor(RoleCode.Owner); return CreateClient(); }
    public HttpClient AsHr() { SetActor(RoleCode.Hr); return CreateClient(); }
    public HttpClient AsDriver() { SetActor(RoleCode.Driver); return CreateClient(); }

    public async Task<VehicleDetail> CreateTruck(HttpClient client, string? plate = null, string? vin = null, string reason = "create reason", int grossWeightKg = 8000)
    {
        var n = Interlocked.Increment(ref sequence);
        plate ??= $"B-T {n:0000}";
        vin ??= $"WVWZZZ1JZXW{n:000000}";
        using var response = await client.PostAsJsonAsync("/api/v1/fleet/vehicles", new
        {
            kind = 0, plate, vin, make = "MAN", model = "TGX", year = 2020, grossWeightKg,
            requiredLicenceClass = "C", ownership = 0, leaseEndDate = (DateOnly?)null, trailerType = (int?)null,
            odometerKm = 1000, depotId = (Guid?)null, serviceIntervalKm = 50000, serviceIntervalMonths = 12,
            lastTachoDownloadAt = (DateOnly?)null, reason
        }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleDetail>(cancellationToken: TestContext.Current.CancellationToken))!;
    }

    public async Task<long> CountRows(string table)
    {
        if (table is not ("vehicles" or "audit_entries" or "audit_entry" or "vehicle_plate_history")) throw new ArgumentException(table);
        await using var connection = new NpgsqlConnection(RuntimeConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = new NpgsqlCommand($"select count(*) from fleetops.{table}", connection);
        return (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    public async Task<NpgsqlConnection> DirectConnection()
    {
        var connection = new NpgsqlConnection(RuntimeConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }

    public string RuntimeConnectionString()
    {
        return postgres?.ConnectionString ?? throw new InvalidOperationException("Database is not initialized.");
    }

    private void SetActor(RoleCode role) => accessor.Current = AccessContext.For(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), role);
    private sealed class MutableAccessor : IAccessContextAccessor { public AccessContext? Current { get; set; } }

    private sealed class FailingVehicleStore(EfVehicleStore inner) : IVehicleStore
    {
        public async Task<VehicleDetail> CreateAsync(CreateVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        { _ = await inner.CreateAsync(command, actor, context, cancellationToken); throw new InvalidOperationException("after write"); }
        public async Task<VehicleDetail> EditAsync(EditVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
        { _ = await inner.EditAsync(command, actor, context, cancellationToken); throw new InvalidOperationException("after write"); }
    }
}
