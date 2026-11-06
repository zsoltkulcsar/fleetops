using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class DocumentQueueTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private readonly Guid employeeId = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        await AddEmployee(employeeId, "Alice", "Driver");
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Upload_appears_in_the_queue_and_the_count_at_once()
    {
        await Upload(factory.AsHr(), "employee", employeeId, "code95");
        var queue = await List(factory.AsHr(), "queue");
        var counts = await factory.AsHr().GetFromJsonAsync<JsonElement>("/api/v1/documents/counts");
        Assert.Single(queue);
        Assert.Equal(1, counts.GetProperty("queue").GetInt32());
    }

    [Fact]
    public async Task Dispatcher_queue_has_only_vehicle_documents()
    {
        using var dispatcher = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(dispatcher);
        await Upload(dispatcher, "vehicle", vehicle.Id, "registration");
        await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using var dispatcherForQueue = factory.AsDispatcher();
        var queue = await List(dispatcherForQueue, "queue", vehicle.Id);
        Assert.Single(queue);
        Assert.Equal("vehicle", queue[0].GetProperty("ownerType").GetString());
    }

    [Fact]
    public async Task Queue_is_oldest_first()
    {
        var first = await Upload(factory.AsHr(), "employee", employeeId, "code95", "FIRST");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        var second = await Upload(factory.AsHr(), "employee", employeeId, "medical", "SECOND");
        var queue = await List(factory.AsHr(), "queue");
        Assert.Equal(first, queue[0].GetProperty("id").GetGuid());
        Assert.Equal(second, queue[1].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Expiring_filter_uses_the_type_lead_days()
    {
        var code95 = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using (await Verify(code95, 1, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(80))) { }
        using var huDispatcher = factory.AsDispatcher();
        var huVehicle = await factory.CreateTruck(huDispatcher);
        var hu = await Upload(huDispatcher, "vehicle", huVehicle.Id, "hu_report");
        using (await Verify(hu, 1, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(40))) { }
        var expiring = await List(factory.AsHr(), "expiring");
        Assert.Contains(expiring, item => item.GetProperty("id").GetGuid() == code95);
        Assert.DoesNotContain(expiring, item => item.GetProperty("id").GetGuid() == hu);
    }

    [Fact]
    public async Task Document_is_valid_through_its_expiry_date()
    {
        var id = await Upload(factory.AsHr(), "employee", employeeId, "code95");
        using (await Verify(id, 1, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime))) { }
        Assert.Empty(await List(factory.AsHr(), "expired"));
        factory.Clock.Advance(TimeSpan.FromDays(1));
        Assert.Contains(await List(factory.AsHr(), "expired"), item => item.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Can_act_matches_the_policy()
    {
        using var dispatcher = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(dispatcher);
        await Upload(dispatcher, "vehicle", vehicle.Id, "registration");
        await Upload(factory.AsHr(), "employee", employeeId, "code95");
        var hrRows = await List(factory.AsHr(), "queue", employeeId);
        using var dispatcherForQueue = factory.AsDispatcher();
        var dispatcherRows = await List(dispatcherForQueue, "queue", vehicle.Id);
        var ownerRows = await List(factory.AsOwner(), "queue");
        Assert.True(hrRows.Single().GetProperty("canAct").GetBoolean());
        Assert.True(dispatcherRows.Single().GetProperty("canAct").GetBoolean());
        Assert.All(ownerRows, item => Assert.False(item.GetProperty("canAct").GetBoolean()));
    }

    [Fact]
    public async Task Search_finds_a_document_number_for_hr()
    {
        await Upload(factory.AsHr(), "employee", employeeId, "code95", "HR-SEARCH");
        using var response = await factory.AsHr().GetAsync("/api/v1/documents?search=HR-SEARCH");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Search_hides_employee_documents_and_people_from_a_dispatcher()
    {
        await Upload(factory.AsHr(), "employee", employeeId, "code95", "PERSON-ONLY");
        using var response = await factory.AsDispatcher().GetAsync("/api/v1/search?q=Alice");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("documents").EnumerateArray());
        Assert.Empty(body.GetProperty("people").EnumerateArray());
    }

    [Fact]
    public async Task Search_refuses_a_driver()
    {
        using var response = await factory.AsDriver().GetAsync("/api/v1/documents?search=AB");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Short_search_returns_nothing()
    {
        using var response = await factory.AsHr().GetAsync("/api/v1/search?q=A");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("documents").EnumerateArray());
        Assert.Empty(body.GetProperty("vehicles").EnumerateArray());
        Assert.Empty(body.GetProperty("people").EnumerateArray());
    }

    private async Task<JsonElement[]> List(HttpClient client, string filter, Guid? ownerId = null)
    {
        var ownerQuery = ownerId.HasValue ? $"&ownerId={ownerId.Value}" : "";
        using var response = await client.GetAsync($"/api/v1/documents?filter={filter}{ownerQuery}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray();
    }

    private async Task<Guid> Upload(HttpClient client, string ownerType, Guid ownerId, string type, string number = "N-123")
    {
        using var linkResponse = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType, ownerId, type, contentType = "application/pdf", sizeBytes = 12 });
        var link = await linkResponse.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions);
        var key = factory.ObjectStore.Linked.Last();
        var fileId = link.GetProperty("fileId").GetGuid();
        factory.ObjectStore.Put(key, 12, "application/pdf");
        using var confirm = await client.PostAsJsonAsync("/api/v1/documents", new { ownerType, ownerId, type, number, issuedAt = (string?)null, expiresAt = (string?)null, categories = (string[]?)null, files = new[] { new { fileId, page = "page" } } });
        Assert.Equal(HttpStatusCode.Created, confirm.StatusCode);
        return (await confirm.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> Verify(Guid id, int version, DateOnly expiresAt) => factory.AsHr().PostAsJsonAsync($"/api/v1/documents/{id}/verify", new { version, expiresAt, issuedAt = (string?)null, number = "VERIFIED", categories = (string[]?)null });

    private async Task AddEmployee(Guid id, string first, string last)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.employees (id, first_name, last_name, email, roles, status, start_date) values (@id, @first, @last, @email, '[]', 'Active', date '2026-01-01')", connection);
        command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("first", first); command.Parameters.AddWithValue("last", last); command.Parameters.AddWithValue("email", $"{id}@test");
        await command.ExecuteNonQueryAsync();
    }
}
