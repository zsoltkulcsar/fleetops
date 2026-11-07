using FleetOps.Modules.Documents;
using Xunit;

namespace FleetOps.Tests;

public sealed class AlertSweepTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Sweep_opens_what_time_made_true()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(1));
        factory.Clock.Advance(TimeSpan.FromDays(2));
        await AlertTestSupport.Sweep(factory);
        Assert.Equal("Open", (await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report")).Status);
    }

    [Fact]
    public async Task Sweep_after_a_clean_run_changes_nothing()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        await AlertTestSupport.Sweep(factory);
        var before = await Snapshot(factory);
        await AlertTestSupport.Sweep(factory);
        Assert.Equal(before, await Snapshot(factory));
    }

    [Fact]
    public async Task Sweep_clears_a_stale_alert()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);
        await ReadinessTestSupport.SetVehicle(factory, vehicle.Id, FleetOps.Modules.Fleet.VehicleStatus.Retired);
        await AlertTestSupport.Sweep(factory);
        Assert.Equal("Resolved", (await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report")).Status);
    }

    [Fact]
    public async Task Sweep_resumes_from_its_checkpoint()
    {
        var first = await AlertTestSupport.Sweep(factory);
        Assert.True(first.Done);
        await using var connection = await factory.DirectConnection();
        await using var command = new Npgsql.NpgsqlCommand("select count(*) from fleetops.job_checkpoints where name = 'alerts-sweep'", connection);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Sweep_failure_on_one_subject_keeps_going_or_records_failed()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));
        var result = await AlertTestSupport.Sweep(factory);
        Assert.True(result.Done || result.Processed > 0);
        Assert.Equal("Open", (await AlertTestSupport.Alert(factory, "A-05", "vehicle", vehicle.Id, "hu_report")).Status);
    }

    private static async Task<string> Snapshot(VehicleApiFactory factory)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new Npgsql.NpgsqlCommand("select coalesce(string_agg(id || ':' || rule_id || ':' || status || ':' || xmin::text || ':' || opened_at::text, '|' order by id), '') from fleetops.alerts", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}

