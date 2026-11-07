using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class DocumentMissingTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private readonly Guid employeeId = Guid.NewGuid();

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        await AddEmployee();
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Missing_lists_a_vehicle_without_a_required_document()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        using var response = await factory.AsHr().GetAsync("/api/v1/documents/missing?limit=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray();
        Assert.Contains(items, item => item.GetProperty("ownerId").GetGuid() == vehicle.Id && item.GetProperty("ownerType").GetString() == "vehicle");
    }

    [Fact]
    public async Task Dispatcher_never_sees_employee_rows_in_missing()
    {
        using var response = await factory.AsDispatcher().GetAsync("/api/v1/documents/missing?limit=100");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray();
        Assert.DoesNotContain(items, item => item.GetProperty("ownerType").GetString() == "employee");
        Assert.DoesNotContain(items, item => item.GetProperty("documentType").GetString() == "medical");
    }

    [Fact]
    public async Task Count_of_missing_matches_the_list()
    {
        using var listResponse = await factory.AsHr().GetAsync("/api/v1/documents/missing?limit=100");
        var list = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        using var countResponse = await factory.AsHr().GetAsync("/api/v1/documents/counts");
        var count = await countResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(list.GetProperty("items").GetArrayLength(), count.GetProperty("missing").GetInt32());
    }

    private async Task AddEmployee()
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.employees (id, first_name, last_name, email, roles, status, start_date) values (@id, 'Alice', 'Driver', @email, '[\"driver\"]', 'Active', date '2026-01-01')", connection);
        command.Parameters.AddWithValue("id", employeeId);
        command.Parameters.AddWithValue("email", $"{employeeId}@test");
        await command.ExecuteNonQueryAsync();
    }
}
