using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

/// <summary>Epic 12 (Stories 12.6, 12.7): owner overview and audit log. Written by the orchestrator (user's choice, 2026-11-12).</summary>
public sealed class OwnerAndAuditTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Owner_overview_has_no_named_driver_hours()
    {
        var driver = await AlertTestSupport.AddEmployee(factory);
        await Execute($"update fleetops.employees set first_name = 'Visible', last_name = 'Drivername' where id = '{driver}'");

        using var owner = await factory.AsOwner().GetAsync("/api/v1/owner/overview");
        Assert.Equal(HttpStatusCode.OK, owner.StatusCode);
        var text = await owner.Content.ReadAsStringAsync();
        var keys = JsonDocument.Parse(text).RootElement.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("key").GetString()).ToArray();
        Assert.Equal(["criticalAlerts", "trucksOffRoad", "documentsCompletePercent", "exportStatus", "maintenanceCostEur"], keys);
        Assert.DoesNotContain("Drivername", text, StringComparison.Ordinal);
        Assert.DoesNotContain(driver.ToString(), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hours", text, StringComparison.OrdinalIgnoreCase);
        using var hr = await factory.AsHr().GetAsync("/api/v1/owner/overview");
        Assert.Equal(HttpStatusCode.Forbidden, hr.StatusCode);
        using var dispatcher = await factory.AsDispatcher().GetAsync("/api/v1/owner/overview");
        Assert.Equal(HttpStatusCode.Forbidden, dispatcher.StatusCode);
    }

    [Fact]
    public async Task Hours_over_48_total_hidden_while_rule_unconfirmed()
    {
        Assert.Equal(0L, await Count("select count(*) from fleetops.rule_confirmations where \"RuleId\" = 'C-08'"));
        var driver = await AlertTestSupport.AddEmployee(factory);
        await Execute($"update fleetops.employees set first_name = 'Long', last_name = 'Week' where id = '{driver}'");

        using var response = await factory.AsOwner().GetAsync("/api/v1/owner/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray().ToArray();
        Assert.DoesNotContain(items, item => item.GetProperty("key").GetString()!.Contains("48", StringComparison.Ordinal));
        Assert.DoesNotContain(items, item => item.GetProperty("key").GetString()!.Contains("week", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, items.Length);
    }

    [Fact]
    public async Task Audit_log_filters_by_person_entity_and_date()
    {
        var person = await AlertTestSupport.AddEmployee(factory);
        var other = await AlertTestSupport.AddEmployee(factory);
        await Audit("Employee", person, "status", "active", "suspended", new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.Zero));
        await Audit("Employee", person, "last_name", "Old", "New", new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        await Audit("Vehicle", Guid.NewGuid(), "plate", "B-X 1", "B-X 2", new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
        await Audit("Employee", other, "status", "active", "left", new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero));
        using var hr = factory.AsHr();

        var byPerson = await Page(hr, $"?PersonId={person}");
        Assert.Equal(2, byPerson.GetProperty("total").GetInt32());
        Assert.All(byPerson.GetProperty("items").EnumerateArray(), row => Assert.Equal(person, row.GetProperty("entityId").GetGuid()));
        var byEntity = await Page(hr, "?EntityType=Vehicle");
        Assert.Equal("plate", Assert.Single(byEntity.GetProperty("items").EnumerateArray()).GetProperty("field").GetString());
        var byDate = await Page(hr, $"?PersonId={person}&From=2026-11-03T00:00:00Z&To=2026-11-05T00:00:00Z");
        Assert.Equal("last_name", Assert.Single(byDate.GetProperty("items").EnumerateArray()).GetProperty("field").GetString());
        using var dispatcher = await factory.AsDispatcher().GetAsync("/api/v1/audit-log");
        Assert.Equal(HttpStatusCode.Forbidden, dispatcher.StatusCode);
        using var invalid = await factory.AsHr().GetAsync("/api/v1/audit-log?From=2026-11-05T00:00:00Z&To=2026-11-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Special_fields_show_only_changed()
    {
        var person = await AlertTestSupport.AddEmployee(factory);
        await Audit("Employee", person, "phone", "+4915100000001", "+4915100000002", new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero), "lost phone at +4915100000001");
        await Audit("Employee", person, "last_name", "Before", "After", new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));

        var page = await Page(factory.AsOwner(), $"?PersonId={person}");
        var rows = page.GetProperty("items").EnumerateArray().ToDictionary(row => row.GetProperty("field").GetString()!);
        Assert.Equal("changed", rows["phone"].GetProperty("oldValue").GetString());
        Assert.Equal("changed", rows["phone"].GetProperty("newValue").GetString());
        Assert.DoesNotContain("4915100000001", page.GetRawText(), StringComparison.Ordinal);
        Assert.Equal("Before", rows["last_name"].GetProperty("oldValue").GetString());
        Assert.Equal("After", rows["last_name"].GetProperty("newValue").GetString());
    }

    [Fact]
    public async Task Audit_export_is_audited()
    {
        var person = await AlertTestSupport.AddEmployee(factory);
        await Audit("Employee", person, "phone", "+4915100000003", "+4915100000004", new DateTimeOffset(2026, 11, 3, 9, 0, 0, TimeSpan.Zero));
        await Audit("Employee", person, "last_name", "=cmd", "After", new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero));
        var before = await Count("select count(*) from fleetops.audit_entries where entity_type = 'AuditLog' and field_name = 'export'");

        using var response = await factory.AsHr().GetAsync($"/api/v1/audit-log/export?PersonId={person}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/csv", response.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
        var csv = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("time,who,entity,entityId,field,old,new,reason", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("4915100000003", csv, StringComparison.Ordinal);
        Assert.Contains("\"'=cmd\"", csv, StringComparison.Ordinal);
        Assert.Equal(before + 1, await Count("select count(*) from fleetops.audit_entries where entity_type = 'AuditLog' and field_name = 'export'"));
        using var dispatcher = await factory.AsDispatcher().GetAsync("/api/v1/audit-log/export");
        Assert.Equal(HttpStatusCode.Forbidden, dispatcher.StatusCode);
        Assert.Equal(before + 1, await Count("select count(*) from fleetops.audit_entries where entity_type = 'AuditLog' and field_name = 'export'"));
    }

    private static async Task<JsonElement> Page(HttpClient client, string query)
    {
        using var response = await client.GetAsync("/api/v1/audit-log" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task Audit(string entityType, Guid entityId, string field, string? oldValue, string? newValue, DateTimeOffset at, string reason = "test change")
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("insert into fleetops.audit_entries (id, entity_type, entity_id, field_name, old_value, new_value, actor_id, reason, created_at) values ($1, $2, $3, $4, $5, $6, $7, $8, $9)", connection);
        command.Parameters.AddWithValue(Guid.NewGuid());
        command.Parameters.AddWithValue(entityType);
        command.Parameters.AddWithValue(entityId);
        command.Parameters.AddWithValue(field);
        command.Parameters.AddWithValue((object?)oldValue ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)newValue ?? DBNull.Value);
        command.Parameters.AddWithValue(VehicleApiFactory.ActorId.ToString());
        command.Parameters.AddWithValue(reason);
        command.Parameters.AddWithValue(at);
        await command.ExecuteNonQueryAsync();
    }

    private async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> Count(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
