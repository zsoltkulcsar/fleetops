using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Modules.Documents;
using Xunit;

namespace FleetOps.Tests;

public sealed class AttentionTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Alerts_on_one_vehicle_form_one_group()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, null, "Uploaded", factory.Clock.GetUtcNow().AddDays(-3));
        await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", vehicle.Id, "hu_report", "Critical", null);
        await AlertTestSupport.AddAlert(factory, "A-03", "document", document, "hu_report", "Warning", null);

        var result = await AttentionTestSupport.Get(factory.AsDispatcher());

        var group = Assert.Single(result.GetProperty("groups").EnumerateArray());
        Assert.Equal(vehicle.Id, group.GetProperty("subjectId").GetGuid());
        Assert.Equal(2, group.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Critical_groups_come_first()
    {
        var warningVehicle = await factory.CreateTruck(factory.AsDispatcher());
        var criticalVehicle = await factory.CreateTruck(factory.AsDispatcher());
        await AlertTestSupport.AddAlert(factory, "A-03", "vehicle", warningVehicle.Id, "registration", "Warning", null);
        await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", criticalVehicle.Id, "hu_report", "Critical", null);

        var groups = (await AttentionTestSupport.Get(factory.AsDispatcher())).GetProperty("groups").EnumerateArray().ToArray();

        Assert.Equal(criticalVehicle.Id, groups[0].GetProperty("subjectId").GetGuid());
    }

    [Fact]
    public async Task Groups_with_equal_severity_order_by_due_date()
    {
        var first = await factory.CreateTruck(factory.AsDispatcher());
        var second = await factory.CreateTruck(factory.AsDispatcher());
        await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", first.Id, "registration", "Warning", DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(2));
        await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", second.Id, "registration", "Warning", DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(1));

        var groups = (await AttentionTestSupport.Get(factory.AsDispatcher())).GetProperty("groups").EnumerateArray().ToArray();

        Assert.Equal(second.Id, groups[0].GetProperty("subjectId").GetGuid());
        Assert.Equal(first.Id, groups[1].GetProperty("subjectId").GetGuid());
    }

    [Fact]
    public async Task Dispatcher_sees_vehicle_alerts_and_no_a02()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        var employee = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", vehicle.Id, "hu_report", "Critical", null);
        await AlertTestSupport.AddAlert(factory, "A-02", "employee", employee, "medical", "Critical", null, "hr");

        var result = await AttentionTestSupport.Get(factory.AsDispatcher());

        Assert.DoesNotContain(result.GetProperty("groups").EnumerateArray(), group => group.GetProperty("subjectId").GetGuid() == employee);
        Assert.Contains(result.GetProperty("groups").EnumerateArray(), group => group.GetProperty("subjectId").GetGuid() == vehicle.Id);
    }

    [Fact]
    public async Task Hr_sees_a02_and_no_a05()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        var employee = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.AddAlert(factory, "A-02", "employee", employee, "medical", "Critical", null, "hr");
        await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", vehicle.Id, "hu_report", "Critical", null);

        var result = await AttentionTestSupport.Get(factory.AsHr());

        Assert.Contains(result.GetProperty("groups").EnumerateArray(), group => group.GetProperty("subjectId").GetGuid() == employee);
        Assert.DoesNotContain(result.GetProperty("groups").EnumerateArray(), group => group.GetProperty("subjectId").GetGuid() == vehicle.Id);
    }

    [Fact]
    public async Task Owner_sees_every_rule()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        var employee = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", vehicle.Id, "hu_report", "Critical", null);
        await AlertTestSupport.AddAlert(factory, "A-02", "employee", employee, "medical", "Critical", null, "hr");

        var result = await AttentionTestSupport.Get(factory.AsOwner());

        Assert.Equal(2, result.GetProperty("groups").GetArrayLength());
    }

    [Fact]
    public async Task Driver_cannot_open_attention()
    {
        using var response = await factory.AsDriver().GetAsync("/api/v1/attention");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dispatcher_never_receives_the_medical_type()
    {
        var employee = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.AddAlert(factory, "A-02", "employee", employee, "medical", "Critical", null, "hr");

        using var response = await factory.AsDispatcher().GetAsync("/api/v1/attention");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("medical", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Resolved_alert_is_gone_from_the_queue()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", vehicle.Id, "hu_report", "Critical", null, status: "Resolved");

        var result = await AttentionTestSupport.Get(factory.AsDispatcher());

        Assert.Empty(result.GetProperty("groups").EnumerateArray());
        Assert.Equal(0, result.GetProperty("totals").GetProperty("open").GetInt32());
    }

    [Fact]
    public async Task Totals_match_what_the_role_sees()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        var employee = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", vehicle.Id, "hu_report", "Critical", null);
        await AlertTestSupport.AddAlert(factory, "A-02", "employee", employee, "medical", "Warning", null, "hr");

        var dispatcher = await AttentionTestSupport.Get(factory.AsDispatcher());
        var hr = await AttentionTestSupport.Get(factory.AsHr());

        Assert.Equal((1, 1), AttentionTestSupport.Totals(dispatcher));
        Assert.Equal((1, 0), AttentionTestSupport.Totals(hr));
    }

    [Fact]
    public async Task Company_empty_is_true_with_no_vehicles_or_drivers()
    {
        var result = await AttentionTestSupport.Get(factory.AsOwner());

        Assert.True(result.GetProperty("companyEmpty").GetBoolean());
    }

    [Fact]
    public async Task Company_empty_is_false_with_one_vehicle()
    {
        await factory.CreateTruck(factory.AsDispatcher());

        var result = await AttentionTestSupport.Get(factory.AsOwner());

        Assert.False(result.GetProperty("companyEmpty").GetBoolean());
    }
}

internal static class AttentionTestSupport
{
    public static async Task<JsonElement> Get(HttpClient client)
    {
        using var response = await client.GetAsync("/api/v1/attention");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions)).Clone();
    }

    public static (int Open, int Critical) Totals(JsonElement result) => (
        result.GetProperty("totals").GetProperty("open").GetInt32(),
        result.GetProperty("totals").GetProperty("critical").GetInt32());
}
