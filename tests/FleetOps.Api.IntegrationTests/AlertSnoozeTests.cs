using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Modules.Documents;
using Xunit;

namespace FleetOps.Tests;

public sealed class AlertSnoozeTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private Guid documentId;

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Warning_snoozes_with_a_reason_and_hides()
    {
        var alert = await AddWarning();
        var until = factory.Clock.GetUtcNow().AddDays(1);

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/alerts/{alert}/snooze", new { version = 1, until, reason = "Waiting for the carrier" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Snoozed", (await AlertTestSupport.Alert(factory, "A-03", "document", documentId)).Status);
        Assert.Empty((await AttentionTestSupport.Get(factory.AsDispatcher())).GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task Snooze_ends_by_itself()
    {
        var alert = await AddWarning();
        await Snooze(alert, factory.Clock.GetUtcNow().AddDays(1), "Temporary delay");
        factory.Clock.Advance(TimeSpan.FromDays(1).Add(TimeSpan.FromMinutes(1)));

        var result = await AttentionTestSupport.Get(factory.AsDispatcher());

        Assert.Single(result.GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task Snooze_past_seven_days_is_refused()
    {
        var alert = await AddWarning();

        using var refused = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/alerts/{alert}/snooze", new { version = 1, until = factory.Clock.GetUtcNow().AddDays(7).AddSeconds(1), reason = "Too long" });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        await AssertField(refused, "until");

        using var accepted = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/alerts/{alert}/snooze", new { version = 1, until = factory.Clock.GetUtcNow().AddDays(7), reason = "Exactly one week" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
    }

    [Fact]
    public async Task Snooze_needs_a_reason()
    {
        var alert = await AddWarning();

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/alerts/{alert}/snooze", new { version = 1, until = factory.Clock.GetUtcNow().AddDays(1), reason = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertField(response, "reason");
    }

    [Fact]
    public async Task Critical_alert_cannot_be_snoozed()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        var alert = await AlertTestSupport.AddAlert(factory, "A-05", "vehicle", vehicle.Id, "hu_report", "Critical", null);

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/alerts/{alert}/snooze", new { version = 1, until = factory.Clock.GetUtcNow().AddDays(1), reason = "Not allowed" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "not_snoozable");
    }

    [Fact]
    public async Task Owner_cannot_snooze()
    {
        var alert = await AddWarning();

        using var response = await factory.AsOwner().PostAsJsonAsync($"/api/v1/alerts/{alert}/snooze", new { version = 1, until = factory.Clock.GetUtcNow().AddDays(1), reason = "Not allowed" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dispatcher_cannot_snooze_what_they_cannot_see()
    {
        var employee = await AlertTestSupport.AddEmployee(factory);
        var alert = await AlertTestSupport.AddAlert(factory, "A-02", "employee", employee, "medical", "Warning", null, "hr");

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/alerts/{alert}/snooze", new { version = 1, until = factory.Clock.GetUtcNow().AddDays(1), reason = "Not visible" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertCode(response, "not_found");
    }

    [Fact]
    public async Task Snooze_with_a_stale_version_conflicts()
    {
        var alert = await AddWarning();
        await Snooze(alert, factory.Clock.GetUtcNow().AddDays(1), "First change");

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/alerts/{alert}/snooze", new { version = 1, until = factory.Clock.GetUtcNow().AddDays(2), reason = "Stale change" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "version_conflict");
    }

    [Fact]
    public async Task Snoozed_alert_survives_a_sweep()
    {
        var alert = await AddWarning();
        await Snooze(alert, factory.Clock.GetUtcNow().AddDays(1), "Awaiting paperwork");
        await AlertTestSupport.Sweep(factory);

        Assert.Equal("Snoozed", (await AlertTestSupport.Alert(factory, "A-03", "document", documentId)).Status);
    }

    [Fact]
    public async Task Unsnooze_shows_it_again()
    {
        var alert = await AddWarning();
        await Snooze(alert, factory.Clock.GetUtcNow().AddDays(1), "Temporary delay");

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/alerts/{alert}/unsnooze", new { version = 2 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single((await AttentionTestSupport.Get(factory.AsDispatcher())).GetProperty("groups").EnumerateArray());
    }

    [Fact]
    public async Task Snooze_is_audited_with_its_reason()
    {
        var alert = await AddWarning();
        await Snooze(alert, factory.Clock.GetUtcNow().AddDays(1), "Carrier promised Friday");

        await using var connection = await factory.DirectConnection();
        await using var command = new Npgsql.NpgsqlCommand("select reason from fleetops.audit_entries where entity_id = $1 and field_name = 'snoozed'", connection);
        command.Parameters.AddWithValue(alert);

        Assert.Equal("Carrier promised Friday", await command.ExecuteScalarAsync());
    }

    private async Task<Guid> AddWarning()
    {
        var vehicle = await factory.CreateTruck(factory.AsDispatcher());
        documentId = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, null, "Uploaded", factory.Clock.GetUtcNow().AddDays(-3));
        return await AlertTestSupport.AddAlert(factory, "A-03", "document", documentId, "hu_report", "Warning", null);
    }

    private async Task Snooze(Guid alert, DateTimeOffset until, string reason)
    {
        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/alerts/{alert}/snooze", new { version = 1, until, reason });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task AssertCode(HttpResponseMessage response, string expected)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions);
        Assert.Equal(expected, body.GetProperty("code").GetString());
    }

    private static async Task AssertField(HttpResponseMessage response, string expected)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions);
        Assert.Contains(expected, body.GetProperty("fields").EnumerateArray().Select(field => field.GetString()));
    }
}
