using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Modules.Fleet;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class VehicleRetireTests : IAsyncLifetime
{
    private const string VehiclesPath = "/api/v1/fleet/vehicles";
    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Dispatcher_retires_with_a_reason()
    {
        using var client = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(client);

        using var response = await Retire(client, vehicle, "sold");
        var result = await Detail(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(VehicleStatus.Retired, result.Status);
        Assert.Equal(RetiredReason.Sold, result.RetiredReason);
        Assert.NotNull(result.RetiredAt);
        Assert.Equal(vehicle.Version + 1, result.Version);
    }

    [Fact]
    public async Task Hr_and_driver_cannot_retire()
    {
        using var owner = factory.AsOwner();
        var vehicle = await factory.CreateTruck(owner);
        using var hr = factory.AsHr();
        using var driver = factory.AsDriver();

        using var hrResponse = await Retire(hr, vehicle, "sold");
        using var driverResponse = await Retire(driver, vehicle, "sold");

        Assert.Equal(HttpStatusCode.Forbidden, hrResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, driverResponse.StatusCode);
        using var ownerAgain = factory.AsOwner();
        var stored = await Get(ownerAgain, vehicle.Id);
        Assert.Equal(VehicleStatus.Active, stored.Status);
        Assert.Equal(vehicle.Version, stored.Version);
    }

    [Fact]
    public async Task Retire_needs_a_valid_reason()
    {
        using var client = factory.AsOwner();
        var missing = await factory.CreateTruck(client);
        var unknown = await factory.CreateTruck(client);

        using var missingResponse = await Retire(client, missing, null);
        using var unknownResponse = await Retire(client, unknown, "unknown");

        Assert.Equal(HttpStatusCode.BadRequest, missingResponse.StatusCode);
        Assert.Contains("retiredReason", await Fields(missingResponse));
        Assert.Equal(HttpStatusCode.BadRequest, unknownResponse.StatusCode);
        Assert.Contains("retiredReason", await Fields(unknownResponse));
    }

    [Fact]
    public async Task Retire_with_a_stale_version_conflicts()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);

        using var response = await Retire(client, vehicle, "sold", vehicle.Version - 1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("version_conflict", await Code(response));
    }

    [Fact]
    public async Task Retired_vehicle_cannot_be_retired_again()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        using var first = await Retire(client, vehicle, "sold");
        var retired = await Detail(first);

        using var response = await Retire(client, retired, "scrapped");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("state_conflict", await Code(response));
    }

    [Fact]
    public async Task Retired_vehicle_cannot_be_edited()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        using var retiredResponse = await Retire(client, vehicle, "sold");
        var retired = await Detail(retiredResponse);

        using var response = await client.PutAsJsonAsync($"{VehiclesPath}/{vehicle.Id}", EditBody(retired, "Changed"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("state_conflict", await Code(response));
    }

    [Fact]
    public async Task Retired_vehicle_leaves_the_list_and_keeps_its_record()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        using var retireResponse = await Retire(client, vehicle, "sold");
        retireResponse.EnsureSuccessStatusCode();

        using var hidden = await client.GetAsync(VehiclesPath, TestContext.Current.CancellationToken);
        using var included = await client.GetAsync($"{VehiclesPath}?includeRetired=true", TestContext.Current.CancellationToken);
        var detail = await Get(client, vehicle.Id);
        var hiddenItems = await Items(hidden);
        var includedItems = await Items(included);

        Assert.DoesNotContain(hiddenItems, item => item.Id == vehicle.Id);
        Assert.Contains(includedItems, item => item.Id == vehicle.Id);
        Assert.NotNull(detail.PlateHistory);
        Assert.NotEmpty(detail.RequiredDocuments);
    }

    [Fact]
    public async Task Retire_writes_audit_entries()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);

        using var response = await Retire(client, vehicle, "sold");
        response.EnsureSuccessStatusCode();
        var rows = await AuditRows(vehicle.Id, "Sold");

        Assert.Contains(rows, row => row.Field == "Status" && row.OldValue == "Active" && row.NewValue == "Retired");
        Assert.Contains(rows, row => row.Field == "RetiredReason" && row.OldValue is null && row.NewValue == "Sold");
    }

    [Fact]
    public async Task Owner_undoes_within_30_days()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        using var retireResponse = await Retire(client, vehicle, "sold");
        var retired = await Detail(retireResponse);
        factory.Clock.Advance(TimeSpan.FromDays(29));

        using var response = await Undo(client, retired, "returned to service");
        var result = await Detail(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(VehicleStatus.Active, result.Status);
        Assert.Null(result.RetiredReason);
        Assert.Null(result.RetiredAt);
        Assert.Equal(retired.Version + 1, result.Version);
    }

    [Fact]
    public async Task Undo_writes_an_audit_entry_with_the_reason()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        using var retireResponse = await Retire(client, vehicle, "sold");
        var retired = await Detail(retireResponse);
        const string reason = "customer returned it";

        using var response = await Undo(client, retired, reason);
        response.EnsureSuccessStatusCode();
        var rows = await AuditRows(vehicle.Id, reason);

        Assert.Contains(rows, row => row.Field == "Status" && row.OldValue == "Retired" && row.NewValue == "Active");
        Assert.Contains(rows, row => row.Field == "RetiredReason" && row.OldValue == "Sold" && row.NewValue is null);
    }

    [Fact]
    public async Task Undo_is_refused_at_exactly_30_days()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        using var retireResponse = await Retire(client, vehicle, "sold");
        var retired = await Detail(retireResponse);
        factory.Clock.Advance(TimeSpan.FromDays(30));

        using var response = await Undo(client, retired, "too late");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("undo_window_closed", await Code(response));
        Assert.Equal(VehicleStatus.Retired, (await Get(client, vehicle.Id)).Status);
    }

    [Fact]
    public async Task Dispatcher_and_hr_cannot_undo()
    {
        using var owner = factory.AsOwner();
        var vehicle = await factory.CreateTruck(owner);
        using var retireResponse = await Retire(owner, vehicle, "sold");
        var retired = await Detail(retireResponse);
        using var dispatcher = factory.AsDispatcher();
        using var hr = factory.AsHr();

        using var dispatcherResponse = await Undo(dispatcher, retired, "not allowed");
        using var hrResponse = await Undo(hr, retired, "not allowed");

        Assert.Equal(HttpStatusCode.Forbidden, dispatcherResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, hrResponse.StatusCode);
        Assert.Equal(VehicleStatus.Retired, (await Get(owner, vehicle.Id)).Status);
    }

    [Fact]
    public async Task Undo_needs_a_reason()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        using var retireResponse = await Retire(client, vehicle, "sold");
        var retired = await Detail(retireResponse);

        using var response = await Undo(client, retired, " ");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("reason", await Fields(response));
    }

    [Fact]
    public async Task Undo_of_an_active_vehicle_conflicts()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);

        using var response = await Undo(client, vehicle, "not retired");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("state_conflict", await Code(response));
    }

    [Fact]
    public async Task Undo_conflicts_when_the_plate_was_reused()
    {
        using var client = factory.AsOwner();
        var first = await factory.CreateTruck(client);
        using var retireResponse = await Retire(client, first, "sold");
        var retired = await Detail(retireResponse);
        var second = await factory.CreateTruck(client, plate: first.Plate);

        using var response = await Undo(client, retired, "reuse plate");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("plate_in_use", await Code(response));
        Assert.Equal(VehicleStatus.Retired, (await Get(client, first.Id)).Status);
        Assert.Equal(first.Plate, second.Plate);
    }

    [Fact]
    public async Task Undo_restores_only_the_vehicle()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client);
        var before = await factory.CountRows("vehicle_plate_history");
        using var retireResponse = await Retire(client, vehicle, "sold");
        var retired = await Detail(retireResponse);

        using var response = await Undo(client, retired, "restore vehicle");
        response.EnsureSuccessStatusCode();

        Assert.Equal(before, await factory.CountRows("vehicle_plate_history"));
    }

    private static Task<HttpResponseMessage> Retire(HttpClient client, VehicleDetail vehicle, string? reason, int? version = null) =>
        client.PostAsJsonAsync($"{VehiclesPath}/{vehicle.Id}/retire", new { version = version ?? vehicle.Version, retiredReason = reason }, TestContext.Current.CancellationToken);

    private static Task<HttpResponseMessage> Undo(HttpClient client, VehicleDetail vehicle, string? reason) =>
        client.PostAsJsonAsync($"{VehiclesPath}/{vehicle.Id}/undo-retire", new { version = vehicle.Version, reason }, TestContext.Current.CancellationToken);

    private static Dictionary<string, object?> EditBody(VehicleDetail vehicle, string make) => new()
    {
        ["version"] = vehicle.Version, ["kind"] = "truck", ["plate"] = vehicle.Plate, ["vin"] = vehicle.Vin,
        ["make"] = make, ["model"] = vehicle.Model, ["year"] = vehicle.Year, ["grossWeightKg"] = vehicle.GrossWeightKg,
        ["requiredLicenceClass"] = vehicle.RequiredLicenceClass, ["ownership"] = "owned", ["leaseEndDate"] = null,
        ["trailerType"] = null, ["odometerKm"] = vehicle.OdometerKm, ["depotId"] = vehicle.DepotId,
        ["serviceIntervalKm"] = vehicle.ServiceIntervalKm, ["serviceIntervalMonths"] = vehicle.ServiceIntervalMonths,
        ["lastTachoDownloadAt"] = vehicle.LastTachoDownloadAt, ["reason"] = "edit",
    };

    private static async Task<VehicleDetail> Detail(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<VehicleDetail>(VehicleApiFactory.JsonOptions, TestContext.Current.CancellationToken))!;

    private static async Task<VehicleDetail> Get(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync($"{VehiclesPath}/{id}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await Detail(response);
    }

    private static async Task<VehicleListItem[]> Items(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<VehicleListResult>(VehicleApiFactory.JsonOptions, TestContext.Current.CancellationToken);
        return result!.Items.ToArray();
    }

    private static async Task<string?> Code(HttpResponseMessage response) =>
        (await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken), cancellationToken: TestContext.Current.CancellationToken)).RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;

    private static async Task<string[]> Fields(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("fields").EnumerateArray().Select(field => field.GetString()!).ToArray();
    }

    private async Task<List<AuditRow>> AuditRows(Guid vehicleId, string reason)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("select field_name, old_value, new_value from fleetops.audit_entries where entity_id = @id and reason = @reason", connection);
        command.Parameters.AddWithValue("id", vehicleId);
        command.Parameters.AddWithValue("reason", reason);
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        var rows = new List<AuditRow>();
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            rows.Add(new(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return rows;
    }

    private sealed record AuditRow(string Field, string? OldValue, string? NewValue);
}
