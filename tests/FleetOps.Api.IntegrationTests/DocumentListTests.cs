using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class DocumentListTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private readonly Guid employeeId = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        await AddEmployee(employeeId, "Alice", "Driver", "Active");
        await AddEmployee(Guid.NewGuid(), "Left", "Person", "Left");
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Dispatcher_sees_only_vehicle_documents()
    {
        var dispatcher = factory.AsDispatcher();
        var vehicle = await factory.CreateTruck(dispatcher);
        await Upload(dispatcher, "vehicle", vehicle.Id, "registration");
        using var hr = factory.AsHr();
        await Upload(hr, "employee", employeeId, "code95");
        dispatcher = factory.AsDispatcher();
        using var response = await dispatcher.GetAsync("/api/v1/documents?limit=2");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(body.RootElement.GetProperty("items").EnumerateArray(), item => Assert.Equal("vehicle", item.GetProperty("ownerType").GetString()));
    }

    [Fact]
    public async Task Dispatcher_gets_404_for_an_employee_document()
    {
        using var hr = factory.AsHr();
        var documentId = await Upload(hr, "employee", employeeId, "code95");
        using var response = await factory.AsDispatcher().GetAsync($"/api/v1/documents/{documentId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Hr_and_owner_see_all()
    {
        using var hr = factory.AsHr();
        await Upload(hr, "employee", employeeId, "code95");
        using var owner = factory.AsOwner();
        using var response = await owner.GetAsync("/api/v1/documents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Driver_cannot_list_documents()
    {
        using var response = await factory.AsDriver().GetAsync("/api/v1/documents");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Search_finds_a_document_number_within_the_role()
    {
        using var hr = factory.AsHr();
        await Upload(hr, "employee", employeeId, "code95", "HR-ONLY");
        using var found = await hr.GetAsync("/api/v1/documents?search=HR-ONLY");
        using var dispatcher = factory.AsDispatcher();
        using var hidden = await dispatcher.GetAsync("/api/v1/documents?search=HR-ONLY");
        Assert.Equal(1, JsonDocument.Parse(await found.Content.ReadAsStringAsync()).RootElement.GetProperty("items").GetArrayLength());
        Assert.Equal(0, JsonDocument.Parse(await hidden.Content.ReadAsStringAsync()).RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Detail_has_no_object_key()
    {
        using var hr = factory.AsHr();
        var documentId = await Upload(hr, "employee", employeeId, "code95");
        var key = factory.ObjectStore.Uploaded.Last().Key;
        using var response = await hr.GetAsync($"/api/v1/documents/{documentId}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(key, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task People_lookup_is_for_hr_and_owner()
    {
        using var hr = factory.AsHr();
        using var allowed = await hr.GetAsync("/api/v1/people/lookup?search=Alice");
        using var dispatcher = factory.AsDispatcher();
        using var refused = await dispatcher.GetAsync("/api/v1/people/lookup?search=Alice");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Contains("id", await allowed.Content.ReadAsStringAsync());
        Assert.Contains("name", await allowed.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.DoesNotContain("Left", await allowed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task People_lookup_finds_one_person_by_id()
    {
        using var response = await factory.AsHr().GetAsync($"/api/v1/people/lookup?id={employeeId}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, items.GetArrayLength());
        Assert.Equal(employeeId, items[0].GetProperty("id").GetGuid());
        Assert.Equal("Alice Driver", items[0].GetProperty("name").GetString());
    }

    private async Task<Guid> Upload(HttpClient client, string ownerType, Guid ownerId, string type, string number = "N-123")
    {
        using var linkResponse = await client.PostAsJsonAsync("/api/v1/documents/upload-links", new { ownerType, ownerId, type, contentType = "application/pdf", sizeBytes = 12 });
        var link = await linkResponse.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions);
        var fileId = link.GetProperty("fileId").GetGuid();
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        using var confirm = await client.PostAsJsonAsync("/api/v1/documents", new { ownerType, ownerId, type, number, issuedAt = (string?)null, expiresAt = (string?)null, categories = (string[]?)null, files = new[] { new { fileId, page = "page" } } });
        var detail = await confirm.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions);
        return detail.GetProperty("id").GetGuid();
    }

    private async Task AddEmployee(Guid id, string firstName, string lastName, string status)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.employees (id, first_name, last_name, email, roles, status, start_date) values (@id, @first, @last, @email, '[]', @status, date '2026-01-01')", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("first", firstName);
        command.Parameters.AddWithValue("last", lastName);
        command.Parameters.AddWithValue("email", $"{id}@test");
        command.Parameters.AddWithValue("status", status);
        await command.ExecuteNonQueryAsync();
    }
}
