using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api.Jobs;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class NotificationTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Reject_stores_a_notification_for_the_uploader()
    {
        var documentId = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, VehicleApiFactory.ActorId, DocumentType.DriverCard, null, "Uploaded");

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/documents/{documentId}/reject", new { version = 1, reason = "private rejection reason" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var row = await NotificationRow(factory, VehicleApiFactory.ActorId);
        Assert.Equal("document_rejected", row.TemplateCode);
        Assert.Null(row.AlertId);
        Assert.Equal(new[] { "documentId", "documentType" }, row.Parameters.RootElement.EnumerateObject().Select(x => x.Name).OrderBy(x => x).ToArray());
        Assert.DoesNotContain("private rejection reason", row.Parameters.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Notification_rolls_back_with_its_command()
    {
        factory.ThrowAfterNotificationWrite = true;
        var documentId = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, VehicleApiFactory.ActorId, DocumentType.DriverCard, null, "Uploaded");

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/documents/{documentId}/reject", new { version = 1, reason = "rejection" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        await using var secondConnection = new NpgsqlConnection(factory.RuntimeConnectionString);
        await secondConnection.OpenAsync();
        await using var command = new NpgsqlCommand("select count(*) from fleetops.notifications", secondConnection);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task New_critical_alert_notifies_each_holder_of_the_role()
    {
        await DeactivateRole(factory, "hr");
        var firstHr = await AlertTestSupport.AddEmployee(factory, "[\"hr\"]");
        var secondHr = await AlertTestSupport.AddEmployee(factory, "[\"hr\"]");
        var driver = await AlertTestSupport.AddEmployee(factory);
        await AddDocumentsExceptMedical(factory, driver);

        await AlertTestSupport.Apply(factory, "employee", driver);

        Assert.Equal(1L, await CountNotifications(factory, firstHr));
        Assert.Equal(1L, await CountNotifications(factory, secondHr));
        var row = await NotificationRow(factory, firstHr);
        Assert.Equal("critical_alert", row.TemplateCode);
        Assert.Equal("A-02", row.Parameters.RootElement.GetProperty("ruleId").GetString());
    }

    [Fact]
    public async Task Alert_race_stores_no_extra_notification()
    {
        await DeactivateRole(factory, "hr");
        var hr = await AlertTestSupport.AddEmployee(factory, "[\"hr\"]");
        var driver = await AlertTestSupport.AddEmployee(factory);
        await AddDocumentsExceptMedical(factory, driver);

        await Task.WhenAll(
            AlertTestSupport.Apply(factory, "employee", driver),
            AlertTestSupport.Apply(factory, "employee", driver));

        Assert.Equal(1L, await CountNotifications(factory, hr));
    }

    [Fact]
    public async Task Reopening_nothing_stores_nothing()
    {
        await DeactivateRole(factory, "hr");
        var hr = await AlertTestSupport.AddEmployee(factory, "[\"hr\"]");
        var driver = await AlertTestSupport.AddEmployee(factory);
        await AddDocumentsExceptMedical(factory, driver);

        await AlertTestSupport.Apply(factory, "employee", driver);
        await AlertTestSupport.Apply(factory, "employee", driver);

        Assert.Equal(1L, await CountNotifications(factory, hr));
    }

    [Fact]
    public async Task Parameters_hold_no_names_numbers_or_links()
    {
        await DeactivateRole(factory, "dispatcher");
        var dispatcher = await AlertTestSupport.AddEmployee(factory, "[\"dispatcher\"]");
        var vehicle = await factory.CreateTruck(factory.AsDispatcher(), "B-NO 1234");
        await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, vehicle.Id, DocumentType.HuReport, DateOnly.FromDateTime(factory.Clock.GetUtcNow().DateTime).AddDays(-1));

        await AlertTestSupport.Apply(factory, "vehicle", vehicle.Id);

        var row = await NotificationRow(factory, dispatcher);
        var keys = row.Parameters.RootElement.EnumerateObject().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Subset(keys, new HashSet<string>(["alertId", "ruleId", "subjectType", "subjectId", "documentType"]));
        Assert.DoesNotContain("B-NO 1234", row.Parameters.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("number", keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("plate", keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", row.Parameters.RootElement.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task In_app_send_sets_sent_at()
    {
        var documentId = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, VehicleApiFactory.ActorId, DocumentType.DriverCard, null, "Uploaded");

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/documents/{documentId}/reject", new { version = 1, reason = "rejection" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await NotificationRow(factory, VehicleApiFactory.ActorId)).SentAt);
    }

    [Fact]
    public async Task Failed_send_leaves_the_command_result_unchanged()
    {
        factory.ThrowNotificationSender = true;
        var documentId = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, VehicleApiFactory.ActorId, DocumentType.DriverCard, null, "Uploaded");

        using var response = await factory.AsDispatcher().PostAsJsonAsync($"/api/v1/documents/{documentId}/reject", new { version = 1, reason = "rejection" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await NotificationRow(factory, VehicleApiFactory.ActorId);
        Assert.Null(row.SentAt);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull(row.NextAttemptAt);
    }

    [Fact]
    public async Task Retry_job_sends_what_is_due()
    {
        var id = await InsertNotification(factory, VehicleApiFactory.ActorId, "critical_alert", "{}", attempts: 1, due: "now() - interval '1 minute'");
        await RunRetryJob();

        Assert.NotNull((await NotificationRow(factory, VehicleApiFactory.ActorId, id)).SentAt);
    }

    [Fact]
    public async Task Retry_job_skips_what_is_not_due()
    {
        var id = await InsertNotification(factory, VehicleApiFactory.ActorId, "critical_alert", "{}", attempts: 1, due: "now() + interval '1 hour'");
        await RunRetryJob();

        var row = await NotificationRow(factory, VehicleApiFactory.ActorId, id);
        Assert.Null(row.SentAt);
        Assert.Equal(1, row.Attempts);
    }

    [Fact]
    public async Task Retry_stops_after_the_max_attempts()
    {
        var id = await InsertNotification(factory, VehicleApiFactory.ActorId, "critical_alert", "{}", attempts: 5, due: "now() - interval '1 minute'");
        await RunRetryJob();

        var row = await NotificationRow(factory, VehicleApiFactory.ActorId, id);
        Assert.Null(row.SentAt);
        Assert.Equal(5, row.Attempts);
    }

    [Fact]
    public async Task Retry_can_run_twice_with_no_change()
    {
        var id = await InsertNotification(factory, VehicleApiFactory.ActorId, "critical_alert", "{}", attempts: 1, due: "now() - interval '1 minute'");
        await RunRetryJob();
        var first = await NotificationRow(factory, VehicleApiFactory.ActorId, id);
        await RunRetryJob();
        var second = await NotificationRow(factory, VehicleApiFactory.ActorId, id);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.SentAt, second.SentAt);
        Assert.Equal(first.Attempts, second.Attempts);
        Assert.Equal(first.NextAttemptAt, second.NextAttemptAt);
    }

    [Fact]
    public async Task Bell_lists_only_my_notifications()
    {
        var other = Guid.NewGuid();
        await AlertTestSupport.AddEmployee(factory, "[\"driver\"]");
        await InsertNotification(factory, VehicleApiFactory.ActorId, "critical_alert", "{}");
        await InsertNotification(factory, other, "critical_alert", "{}");

        using var response = await factory.AsDispatcher().GetAsync("/api/v1/notifications");
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, result.GetProperty("items").GetArrayLength());
        Assert.Equal(1, result.GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task Read_of_another_persons_notification_is_404()
    {
        var other = Guid.NewGuid();
        var id = await InsertNotification(factory, other, "critical_alert", "{}");

        using var response = await factory.AsDispatcher().PostAsync($"/api/v1/notifications/{id}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null((await NotificationRow(factory, other, id)).ReadAt);
    }

    [Fact]
    public async Task Read_all_marks_only_mine()
    {
        var other = Guid.NewGuid();
        var mine = await InsertNotification(factory, VehicleApiFactory.ActorId, "critical_alert", "{}");
        var theirs = await InsertNotification(factory, other, "critical_alert", "{}");

        using var response = await factory.AsDispatcher().PostAsync("/api/v1/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull((await NotificationRow(factory, VehicleApiFactory.ActorId, mine)).ReadAt);
        Assert.Null((await NotificationRow(factory, other, theirs)).ReadAt);
    }

    [Fact]
    public async Task Unread_count_falls_after_read()
    {
        var id = await InsertNotification(factory, VehicleApiFactory.ActorId, "critical_alert", "{}");
        using var before = await factory.AsDispatcher().GetAsync("/api/v1/notifications");
        Assert.Equal(1, (await before.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unread").GetInt32());

        using var read = await factory.AsDispatcher().PostAsync($"/api/v1/notifications/{id}/read", null);
        using var after = await factory.AsDispatcher().GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(0, (await after.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("unread").GetInt32());
    }

    [Fact]
    public async Task Notification_response_is_not_cached()
    {
        using var response = await factory.AsDispatcher().GetAsync("/api/v1/notifications");

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunRetryJob()
    {
        using var scope = factory.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<IEnumerable<IJob>>().Single(x => x.Name == "notifications-retry");
        await job.RunStepAsync(new JobContext(null, factory.Clock.GetUtcNow(), new SystemActor("test")), CancellationToken.None);
    }

    private static async Task DeactivateRole(VehicleApiFactory factory, string role)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("update fleetops.employees set status = 'Inactive' where roles::jsonb @> $1::jsonb", connection);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(new[] { role }));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AddDocumentsExceptMedical(VehicleApiFactory factory, Guid employeeId)
    {
        await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, employeeId, DocumentType.LicenceC_CE, null);
        await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, employeeId, DocumentType.DriverCard, null);
        await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, employeeId, DocumentType.Code95, null);
    }

    private static async Task<long> CountNotifications(VehicleApiFactory factory, Guid? employeeId = null)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("select count(*) from fleetops.notifications where ($1::uuid is null or employee_id = $1)", connection);
        command.Parameters.AddWithValue(employeeId is null ? DBNull.Value : employeeId.Value);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<Guid[]> NotificationEmployees(VehicleApiFactory factory)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("select employee_id from fleetops.notifications order by employee_id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var employees = new List<Guid>();
        while (await reader.ReadAsync()) employees.Add(reader.GetGuid(0));
        return employees.ToArray();
    }

    private static async Task<Guid[]> EmployeesWithRole(VehicleApiFactory factory, string role)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("select id from fleetops.employees where roles::jsonb @> $1::jsonb and status = 'Active'", connection);
        command.Parameters.AddWithValue(JsonSerializer.Serialize(new[] { role }));
        await using var reader = await command.ExecuteReaderAsync();
        var employees = new List<Guid>();
        while (await reader.ReadAsync()) employees.Add(reader.GetGuid(0));
        return employees.ToArray();
    }

    private static async Task<Guid> InsertNotification(VehicleApiFactory factory, Guid employeeId, string templateCode, string parameters, int attempts = 0, string due = "null")
    {
        var id = Guid.NewGuid();
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand($"insert into fleetops.notifications (id, employee_id, template_code, parameters, channel, created_at, attempts, next_attempt_at) values ($1, $2, $3, $4::jsonb, 'in_app', now(), $5, {due})", connection);
        command.Parameters.AddWithValue(id);
        command.Parameters.AddWithValue(employeeId);
        command.Parameters.AddWithValue(templateCode);
        command.Parameters.AddWithValue(parameters);
        command.Parameters.AddWithValue(attempts);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task<NotificationSnapshot> NotificationRow(VehicleApiFactory factory, Guid employeeId, Guid? id = null)
    {
        await using var connection = await factory.DirectConnection();
        await using var command = new NpgsqlCommand("select id, template_code, alert_id, parameters::text, sent_at, read_at, attempts, next_attempt_at from fleetops.notifications where employee_id = $1 and ($2::uuid is null or id = $2) order by created_at desc limit 1", connection);
        command.Parameters.AddWithValue(employeeId);
        command.Parameters.AddWithValue(id is null ? DBNull.Value : id.Value);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync(), "Missing notification row");
        return new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetGuid(2), JsonDocument.Parse(reader.GetString(3)), reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7));
    }

    private sealed record NotificationSnapshot(Guid Id, string TemplateCode, Guid? AlertId, JsonDocument Parameters, DateTimeOffset? SentAt, DateTimeOffset? ReadAt, int Attempts, DateTimeOffset? NextAttemptAt);
}
