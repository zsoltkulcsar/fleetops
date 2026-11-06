using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Modules.Fleet;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class VehicleListTests : IAsyncLifetime
{
    private const string VehiclesPath = "/api/v1/fleet/vehicles";

    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task List_returns_the_first_page_in_plate_order()
    {
        using var client = factory.AsOwner();
        await CreateTruck(client, "A-X 0002");
        await CreateTruck(client, "A-X 0001");
        await CreateTruck(client, "A-X 0003");

        var result = await List(client, limit: 2);

        Assert.Equal(["A-X 0001", "A-X 0002"], result.Items.Select(item => item.Plate).ToArray());
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task Paging_reaches_every_row_exactly_once()
    {
        using var client = factory.AsOwner();
        for (var index = 1; index <= 120; index++)
            await CreateTruck(client, $"A-X {index:0000}");

        var items = await ReadAll(client, 17);

        Assert.Equal(120, items.Count);
        Assert.Equal(120, items.Select(item => item.Id).Distinct().Count());
        Assert.Equal(Enumerable.Range(1, 120).Select(index => $"A-X {index:0000}"), items.Select(item => item.Plate));
    }

    [Fact]
    public async Task Rows_added_while_paging_do_not_repeat_earlier_rows()
    {
        using var client = factory.AsOwner();
        for (var index = 1; index <= 4; index++)
            await CreateTruck(client, $"A-X {index:0000}");

        var first = await List(client, limit: 2);
        await CreateTruck(client, "A-X 0005");

        var rest = await ReadAll(client, 2, first.NextCursor);

        Assert.Empty(first.Items.Select(item => item.Id).Intersect(rest.Select(item => item.Id)));
        Assert.Equal(["A-X 0003", "A-X 0004", "A-X 0005"], rest.Select(item => item.Plate).ToArray());
    }

    [Fact]
    public async Task Limit_is_capped_at_100()
    {
        using var client = factory.AsOwner();
        for (var index = 1; index <= 101; index++)
            await CreateTruck(client, $"A-X {index:0000}");

        var result = await List(client, limit: 1000);

        Assert.Equal(100, result.Items.Count);
        Assert.NotNull(result.NextCursor);
    }

    [Fact]
    public async Task Search_matches_part_of_a_plate_in_any_case()
    {
        using var client = factory.AsOwner();
        await CreateTruck(client, "B-X 0001");
        await CreateTruck(client, "C-X 0002");

        var result = await List(client, search: "b-x 00");

        Assert.Equal(["B-X 0001"], result.Items.Select(item => item.Plate).ToArray());
    }

    [Fact]
    public async Task Search_treats_percent_and_underscore_as_text()
    {
        using var client = factory.AsOwner();
        await CreateTruck(client, "B-X 0001");
        await CreateTruck(client, "B-X 0002");

        Assert.Empty((await List(client, search: "B-X%" )).Items);
        Assert.Empty((await List(client, search: "B-X_" )).Items);
    }

    [Fact]
    public async Task Search_cannot_inject_sql()
    {
        using var client = factory.AsOwner();
        await CreateTruck(client, "B-X 0001");

        var result = await List(client, search: "' OR 1=1 --");

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Kind_filter_returns_only_that_kind()
    {
        using var client = factory.AsOwner();
        await CreateTruck(client, "A-X 0001");
        await CreateTrailer(client, "A-Y 0001");

        var result = await List(client, kind: "Trailer");

        Assert.Single(result.Items);
        Assert.Equal(VehicleKind.Trailer, result.Items[0].Kind);
    }

    [Fact]
    public async Task Undefined_kind_is_refused()
    {
        using var client = factory.AsOwner();

        using var response = await client.GetAsync($"{VehiclesPath}?kind=plane", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("kind", (await Fields(response))[0]);
    }

    [Fact]
    public async Task Bad_cursor_is_refused()
    {
        using var client = factory.AsOwner();

        using var response = await client.GetAsync($"{VehiclesPath}?cursor=not-a-cursor", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_cursor", await Code(response));
    }

    [Fact]
    public async Task Retired_vehicles_are_excluded_by_default()
    {
        using var client = factory.AsOwner();
        var vehicle = await factory.CreateTruck(client, "A-X 0001");
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("update fleetops.vehicles set status = 'Retired' where id = @id", connection);
        command.Parameters.AddWithValue("id", vehicle.Id);
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

        var result = await List(client);

        Assert.DoesNotContain(result.Items, item => item.Id == vehicle.Id);
    }

    [Fact]
    public async Task Hr_and_owner_can_list()
    {
        using var owner = factory.AsOwner();
        var vehicle = await factory.CreateTruck(owner, "A-X 0001");
        using var hr = factory.AsHr();

        var ownerResult = await List(owner);
        var hrResult = await List(hr);

        Assert.Contains(ownerResult.Items, item => item.Id == vehicle.Id);
        Assert.Contains(hrResult.Items, item => item.Id == vehicle.Id);
    }

    [Fact]
    public async Task Driver_cannot_list()
    {
        using var client = factory.AsDriver();

        using var response = await client.GetAsync(VehiclesPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("permission_denied", await Code(response));
    }

    [Fact]
    public async Task Anonymous_cannot_list()
    {
        using var client = factory.Anonymous();

        using var response = await client.GetAsync(VehiclesPath, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("not_signed_in", await Code(response));
    }

    [Fact]
    public async Task List_item_holds_no_vin()
    {
        using var client = factory.AsOwner();
        await factory.CreateTruck(client, "A-X 0001");

        var json = await client.GetStringAsync(VehiclesPath, TestContext.Current.CancellationToken);

        Assert.DoesNotContain("vin", json, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<VehicleListResult> List(HttpClient client, int? limit = null, string? search = null, string? kind = null, string? cursor = null)
    {
        var query = new List<string>();
        if (limit is not null) query.Add($"limit={limit}");
        if (search is not null) query.Add($"search={Uri.EscapeDataString(search)}");
        if (kind is not null) query.Add($"kind={Uri.EscapeDataString(kind)}");
        if (cursor is not null) query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        var path = query.Count == 0 ? VehiclesPath : $"{VehiclesPath}?{string.Join('&', query)}";
        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VehicleListResult>(VehicleApiFactory.JsonOptions, TestContext.Current.CancellationToken))!;
    }

    private async Task<List<VehicleListItem>> ReadAll(HttpClient client, int limit, string? cursor = null)
    {
        var all = new List<VehicleListItem>();
        do
        {
            var page = await List(client, limit, cursor: cursor);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        }
        while (cursor is not null);
        return all;
    }

    private async Task<VehicleDetail> CreateTruck(HttpClient client, string plate)
    {
        return await factory.CreateTruck(client, plate, factory.NextVin());
    }

    private async Task<VehicleListItem> CreateTrailer(HttpClient client, string plate)
    {
        using var response = await client.PostAsJsonAsync(VehiclesPath, new
        {
            kind = "trailer",
            plate,
            vin = factory.NextVin(),
            make = "SCHMITZ",
            model = "S.KO",
            year = 2020,
            grossWeightKg = 8000,
            requiredLicenceClass = (string?)null,
            ownership = "owned",
            leaseEndDate = (DateOnly?)null,
            trailerType = "curtainsider",
            odometerKm = (int?)null,
            depotId = (Guid?)null,
            serviceIntervalKm = (int?)null,
            serviceIntervalMonths = (int?)null,
            lastTachoDownloadAt = (DateOnly?)null,
            reason = "list fixture",
        }, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var detail = (await response.Content.ReadFromJsonAsync<VehicleDetail>(VehicleApiFactory.JsonOptions, TestContext.Current.CancellationToken))!;
        return new(detail.Id, detail.Kind, detail.Plate, detail.Make, detail.Model, detail.Year, detail.Status, detail.Ownership, detail.Version);
    }

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static async Task<string[]> Fields(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("fields").EnumerateArray().Select(field => field.GetString()!).ToArray();
    }
}
