using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace FleetOps.Tests;

public sealed class VehicleWireFormatTests : IAsyncLifetime
{
    private const string VehiclesPath = "/api/v1/fleet/vehicles";

    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task List_sends_enums_as_snake_case_strings()
    {
        using var client = factory.AsOwner();
        await Post(client, CreateBody(kind: "truck", ownership: "rented_in"));

        using var response = await client.GetAsync(VehiclesPath, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var item = document.RootElement.GetProperty("items")[0];

        Assert.Equal(JsonValueKind.String, item.GetProperty("kind").ValueKind);
        Assert.Equal("truck", item.GetProperty("kind").GetString());
        Assert.Equal("active", item.GetProperty("status").GetString());
        Assert.Equal("rented_in", item.GetProperty("ownership").GetString());
    }

    [Fact]
    public async Task Detail_sends_enums_as_snake_case_strings()
    {
        using var client = factory.AsOwner();
        using var created = await Post(client, CreateBody(kind: "trailer", trailerType: "box", odometerKm: null));
        created.EnsureSuccessStatusCode();
        using var createdDocument = JsonDocument.Parse(await created.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var id = createdDocument.RootElement.GetProperty("id").GetGuid();

        using var response = await client.GetAsync($"{VehiclesPath}/{id}", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var root = document.RootElement;

        Assert.Equal("trailer", root.GetProperty("kind").GetString());
        Assert.Equal("box", root.GetProperty("trailerType").GetString());
        Assert.Equal(JsonValueKind.String, root.GetProperty("ownership").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("plateHistory").ValueKind);
        Assert.All(root.GetProperty("plateHistory").EnumerateArray(), plate => Assert.Equal(JsonValueKind.String, plate.ValueKind));
        Assert.NotEmpty(root.GetProperty("requiredDocuments").EnumerateArray());
        Assert.All(root.GetProperty("requiredDocuments").EnumerateArray(), requiredDocument =>
        {
            Assert.Equal(JsonValueKind.String, requiredDocument.GetProperty("type").ValueKind);
            Assert.Contains(requiredDocument.GetProperty("needsConfirmation").ValueKind, new[] { JsonValueKind.False, JsonValueKind.True });
        });
    }

    [Fact]
    public async Task Integer_enum_in_a_body_is_refused()
    {
        using var client = factory.AsOwner();
        var before = await factory.CountRows("vehicles");
        var body = CreateBody();
        body["kind"] = 0;

        using var response = await Post(client, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal("validation_error", document.RootElement.GetProperty("code").GetString());
        Assert.Contains("kind", document.RootElement.GetProperty("fields").EnumerateArray().Select(field => field.GetString()));
        Assert.Equal(before, await factory.CountRows("vehicles"));
    }

    [Fact]
    public async Task Malformed_json_body_returns_a_problem_without_parser_detail()
    {
        using var client = factory.AsOwner();
        using var content = new StringContent("this is not JSON", Encoding.UTF8, "application/json");

        using var response = await client.PostAsync(VehiclesPath, content, TestContext.Current.CancellationToken);
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("System.Text.Json", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Path:", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Kind_filter_accepts_the_wire_value()
    {
        using var client = factory.AsOwner();
        using var truck = await Post(client, CreateBody(kind: "truck"));
        using var trailer = await Post(client, CreateBody(kind: "trailer", trailerType: "box", odometerKm: null));

        using var response = await client.GetAsync($"{VehiclesPath}?kind=trailer", TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToArray();

        Assert.Single(items);
        Assert.Equal("trailer", items[0].GetProperty("kind").GetString());
    }

    private Dictionary<string, object?> CreateBody(
        string kind = "truck",
        string ownership = "owned",
        string? trailerType = null,
        int? odometerKm = 1000) => new()
    {
        ["kind"] = kind,
        ["plate"] = factory.NextPlate(),
        ["vin"] = factory.NextVin(),
        ["make"] = "MAN",
        ["model"] = "TGX",
        ["year"] = 2020,
        ["grossWeightKg"] = 8000,
        ["requiredLicenceClass"] = kind == "truck" ? "C" : null,
        ["ownership"] = ownership,
        ["leaseEndDate"] = ownership == "owned" ? null : "2030-01-01",
        ["trailerType"] = trailerType,
        ["odometerKm"] = kind == "truck" ? odometerKm : null,
        ["depotId"] = null,
        ["serviceIntervalKm"] = kind == "truck" ? 50000 : null,
        ["serviceIntervalMonths"] = kind == "truck" ? 12 : null,
        ["lastTachoDownloadAt"] = null,
        ["reason"] = "wire format test",
    };

    private static Task<HttpResponseMessage> Post(HttpClient client, Dictionary<string, object?> body) =>
        client.PostAsJsonAsync(VehiclesPath, body, TestContext.Current.CancellationToken);
}
