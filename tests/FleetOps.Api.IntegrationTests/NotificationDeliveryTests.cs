using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api.Jobs;
using FleetOps.Identity;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

/// <summary>Epic 12 (Stories 12.1–12.5): delivery, language and masking, SMS limit, driver triggers, digest,
/// preferences, escalation and cover. Written by the orchestrator (user's choice, 2026-11-12).</summary>
public sealed class NotificationDeliveryTests : IAsyncLifetime
{
    // Valid-length VAPID keys for tests only (65 and 32 bytes, base64url): push becomes enabled.
    private static readonly string PublicKey = Base64Url(Enumerable.Range(1, 65).Select(value => (byte)value).ToArray());
    private static readonly string PrivateKey = Base64Url(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
    private readonly VehicleApiFactory factory = new();

    public ValueTask InitializeAsync() => factory.InitializeAsync();

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Critical_alert_sends_push_and_email_after_commit()
    {
        await using var host = WithPush();
        await DeactivateRole("hr");
        var hr = await OfficePerson("[\"hr\"]", "en");
        await Subscribe(host, hr);
        var driver = await AlertTestSupport.AddEmployee(factory);
        await AddDriverDocumentsExcept(driver, DocumentType.Medical);

        // The sweep runs inside the push-enabled host, so the alert opening queues push as well as e-mail.
        await RunJob(host, "alerts-sweep");
        await RunJob(host, "notifications-retry");

        var emailId = Assert.Single(await DriverAlertNotifications(hr, driver, "email"));
        var email = Assert.Single(Emails(host), message => message.NotificationId == emailId);
        Assert.Equal($"{hr}@alert.test", email.Address);
        var pushId = Assert.Single(await DriverAlertNotifications(hr, driver, "push"));
        var push = Assert.Single(Pushes(host), message => message.NotificationId == pushId);
        Assert.Equal("https://push.example.test/" + hr, push.Endpoint);
        Assert.Equal(2L, await Count($"select count(*) from fleetops.notifications where id in ('{emailId}', '{pushId}') and sent_at is not null"));
    }

    [Fact]
    public async Task Failed_delivery_is_retried_by_the_job()
    {
        await DeactivateRole("hr");
        var hr = await OfficePerson("[\"hr\"]", "en");
        // The e-mail address comes from the office login.
        await Execute($"update fleetops.users set email = null where employee_id = '{hr}'");
        var driver = await AlertTestSupport.AddEmployee(factory);
        await AddDriverDocumentsExcept(driver, DocumentType.Medical);
        await AlertTestSupport.Apply(factory, "employee", driver);
        await RunJob(factory, "notifications-retry");

        var emailRow = $"select attempts from fleetops.notifications where employee_id = '{hr}' and channel = 'email'";
        Assert.Equal(1L, Convert.ToInt64(await Scalar(emailRow)));
        Assert.DoesNotContain(Emails(factory), message => message.EmployeeId == hr);

        await Execute($"update fleetops.users set email = '{hr}@alert.test' where employee_id = '{hr}'");
        factory.Clock.Advance(TimeSpan.FromMinutes(2));
        await RunJob(factory, "notifications-retry");

        Assert.Single(Emails(factory), message => message.EmployeeId == hr);
        Assert.Equal(1L, await Count($"select count(*) from fleetops.notifications where employee_id = '{hr}' and channel = 'email' and sent_at is not null"));
    }

    [Fact]
    public async Task Email_uses_language_pref_and_masks_medical()
    {
        await DeactivateRole("hr");
        var german = await OfficePerson("[\"hr\"]", "de");
        var driver = await AlertTestSupport.AddEmployee(factory);
        await AddDriverDocumentsExcept(driver, DocumentType.Medical);

        await AlertTestSupport.Apply(factory, "employee", driver);
        await RunJob(factory, "notifications-retry");

        var email = Assert.Single(Emails(factory), message => message.EmployeeId == german);
        Assert.Equal("FleetOps: Aktion erforderlich", email.Subject);
        Assert.DoesNotContain("medical", email.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Untersuchung", email.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Alert Test", email.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Push_and_sms_bodies_are_generic()
    {
        await using var host = WithPush();
        await DeactivateRole("hr");
        var hr = await OfficePerson("[\"hr\"]", "en");
        await Subscribe(host, hr);
        var driver = await AlertTestSupport.AddEmployee(factory);
        await AddDriverDocumentsExcept(driver, DocumentType.Medical);

        await RunJob(host, "alerts-sweep");
        await RunJob(host, "notifications-retry");

        var pushId = Assert.Single(await DriverAlertNotifications(hr, driver, "push"));
        var push = Assert.Single(Pushes(host), message => message.NotificationId == pushId);
        Assert.Equal("FleetOps: action needed", push.Body);
        var sms = new RecordingSms();
        using var actor = factory.AsHr();
        await using var smsHost = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEnrolmentSms>();
            services.AddSingleton<IEnrolmentSms>(sms);
        }));
        await Execute($"update fleetops.employees set phone = '+4915100000001', personnel_number = 'P-SMS-1' where id = '{driver}'");
        using var client = smsHost.CreateClient();
        FleetRequestHeaders.Apply(client);
        using (var response = await client.PostAsync($"/api/v1/people/{driver}/enrolment", null))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sent = Assert.Single(sms.Sent);
        Assert.Equal("+4915100000001", sent.Phone);
        // The SMS carries only the code: no name, no document type.
        Assert.Matches("^[0-9]{6}$", sent.Message);
    }

    [Fact]
    public async Task Sms_daily_limit_fails_closed()
    {
        await AlertTestSupport.SetSetting(factory, "sms.daily_limit", 1);
        // The Demo SMS adapter delivers only to its test inbox; the limit wraps it.
        await using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Enrolment:TestInbox", "true"));
        using var scope = host.Services.CreateScope();
        var sms = scope.ServiceProvider.GetRequiredService<IEnrolmentSms>();
        const string used = "select coalesce(sum(count), 0) from fleetops.sms_daily_usage";

        await sms.SendAsync("+4915100000011", "111111", CancellationToken.None);
        Assert.Equal(1L, await Count(used));
        var refused = await Assert.ThrowsAsync<SmsLimitReachedException>(() => sms.SendAsync("+4915100000012", "222222", CancellationToken.None));

        Assert.Equal("sms_limit_reached", refused.Message);
        Assert.Equal(1L, await Count(used));
        await AlertTestSupport.SetSetting(factory, "sms.daily_limit", 0);
        await Assert.ThrowsAsync<SmsLimitReachedException>(() => sms.SendAsync("+4915100000013", "333333", CancellationToken.None));
        Assert.Equal(1L, await Count(used));
    }

    [Fact]
    public async Task Drivers_are_told_only_about_their_own_items()
    {
        await DeactivateRole("hr");
        var hr = await OfficePerson("[\"hr\"]", "en");
        var first = await AlertTestSupport.AddEmployee(factory);
        var second = await AlertTestSupport.AddEmployee(factory);
        var document = await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, first, DocumentType.DriverCard, new DateOnly(2027, 1, 1), "Uploaded");
        await Execute($"update fleetops.documents set created_by = '{first}' where id = '{document}'");

        using var response = await factory.AsHr(hr).PostAsJsonAsync($"/api/v1/documents/{document}/reject", new { version = 1, reason = "blurred photo" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AlertTestSupport.Apply(factory, "employee", first);

        Assert.Equal(1L, await Count($"select count(*) from fleetops.notifications where employee_id = '{first}' and template_code = 'document_rejected'"));
        Assert.Equal(0L, await Count($"select count(*) from fleetops.notifications where employee_id = '{second}'"));
    }

    [Fact]
    public async Task Chasing_goes_to_the_office_not_the_driver()
    {
        await DeactivateRole("hr");
        await DeactivateRole("dispatcher");
        var hr = await OfficePerson("[\"hr\"]", "en");
        var dispatcher = await OfficePerson("[\"dispatcher\"]", "en");
        var driver = await AlertTestSupport.AddEmployee(factory);
        var alert = await AlertTestSupport.AddAlert(factory, "A-02", "employee", driver, "medical", "Critical", null, assignedRole: "hr");
        await Execute($"update fleetops.alerts set opened_at = '{factory.Clock.GetUtcNow().AddHours(-1):O}' where id = '{alert}'");
        var before = await Count($"select count(*) from fleetops.notifications where employee_id = '{driver}'");

        await RunJob(factory, "alerts-escalation");

        Assert.Equal(before, await Count($"select count(*) from fleetops.notifications where employee_id = '{driver}'"));
        Assert.Equal(1L, await Count($"select count(*) from fleetops.notifications where employee_id = '{dispatcher}' and template_code = 'alert_escalated' and channel = 'in_app'"));
        Assert.Equal(0L, await Count($"select count(*) from fleetops.notifications where employee_id = '{hr}' and template_code = 'alert_escalated'"));
    }

    [Fact]
    public async Task Digest_is_masked_and_skipped_when_empty()
    {
        await DeactivateRole("hr");
        var busy = await OfficePerson("[\"hr\"]", "en");
        // A dispatcher whose queue is empty: A-01 at 11 days out is HR-only (dispatchers see it from 7 days).
        await DeactivateRole("dispatcher");
        var quiet = await OfficePerson("[\"dispatcher\"]", "en");
        var driver = await AlertTestSupport.AddEmployee(factory);
        await AlertTestSupport.AddAlert(factory, "A-01", "employee", driver, "medical", "Warning", new DateOnly(2026, 11, 20), assignedRole: "hr");
        await Execute("delete from fleetops.alerts where assigned_role = 'dispatcher'");
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 9, 6, 30, 0, TimeSpan.Zero));

        await RunJob(factory, "notifications-digest");
        await RunJob(factory, "notifications-retry");

        var digest = Assert.Single(Emails(factory), message => message.EmployeeId == busy);
        Assert.DoesNotContain("medical", digest.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Emails(factory), message => message.EmployeeId == quiet);
        Assert.Equal(0L, await Count($"select count(*) from fleetops.notifications where employee_id = '{quiet}' and template_code = 'daily_digest'"));
    }

    [Fact]
    public async Task Notify_prefs_apply_to_that_user_only()
    {
        await DeactivateRole("hr");
        var quiet = await OfficePerson("[\"hr\"]", "en");
        var loud = await OfficePerson("[\"hr\"]", "en");
        using (var response = await factory.AsHr(quiet).PutAsJsonAsync("/api/v1/me/notify-prefs", new { criticalPush = true, criticalEmail = false, digestEmail = true }))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var driver = await AlertTestSupport.AddEmployee(factory);

        await AlertTestSupport.Apply(factory, "employee", driver);
        await RunJob(factory, "notifications-retry");

        Assert.DoesNotContain(Emails(factory), message => message.EmployeeId == quiet);
        Assert.Contains(Emails(factory), message => message.EmployeeId == loud);
        using var mine = await factory.AsHr(loud).GetAsync("/api/v1/me/notify-prefs");
        Assert.True((await mine.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("criticalEmail").GetBoolean());
        Assert.Equal(1L, await Count($"select count(*) from fleetops.users where employee_id = '{quiet}' and critical_email = false"));
    }

    [Fact]
    public async Task Escalation_tells_owner_and_next_role_once()
    {
        await DeactivateRole("owner");
        await DeactivateRole("dispatcher");
        var owner = await OfficePerson("[\"owner\"]", "en");
        var dispatcher = await OfficePerson("[\"dispatcher\"]", "en");
        var driver = await AlertTestSupport.AddEmployee(factory);
        var fresh = await AlertTestSupport.AddAlert(factory, "A-02", "employee", driver, "licence_c_ce", "Critical", null, assignedRole: "hr");
        var old = await AlertTestSupport.AddAlert(factory, "A-02", "employee", driver, "medical", "Critical", null, assignedRole: "hr");
        await Execute($"update fleetops.alerts set opened_at = '{factory.Clock.GetUtcNow().AddMinutes(-16):O}' where id = '{old}'");
        await Execute($"update fleetops.alerts set opened_at = '{factory.Clock.GetUtcNow().AddMinutes(-14):O}' where id = '{fresh}'");

        await RunJob(factory, "alerts-escalation");
        await RunJob(factory, "alerts-escalation");

        Assert.Equal(1L, await Count($"select count(*) from fleetops.notifications where employee_id = '{owner}' and template_code = 'alert_escalated' and channel = 'in_app'"));
        Assert.Equal(1L, await Count($"select count(*) from fleetops.notifications where employee_id = '{dispatcher}' and template_code = 'alert_escalated' and channel = 'in_app'"));
        Assert.NotNull(await Scalar($"select escalated_at from fleetops.alerts where id = '{old}'"));
        Assert.Equal(DBNull.Value, await Scalar($"select escalated_at from fleetops.alerts where id = '{fresh}'"));
    }

    [Fact]
    public async Task Cover_routes_alerts_to_the_colleague_during_the_dates()
    {
        await DeactivateRole("hr");
        var away = await OfficePerson("[\"hr\"]", "en");
        var colleague = await OfficePerson("[\"dispatcher\"]", "en");
        var today = new DateOnly(2026, 11, 6);
        using (var response = await factory.AsHr(away).PostAsJsonAsync("/api/v1/me/alert-covers", new { toId = colleague, from = today, to = today.AddDays(2) }))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var first = await AlertTestSupport.AddEmployee(factory);
        await AddDriverDocumentsExcept(first, DocumentType.Medical);

        await AlertTestSupport.Apply(factory, "employee", first);
        Assert.Equal(1L, await Count($"select count(*) from fleetops.notifications where employee_id = '{colleague}' and template_code = 'critical_alert' and channel = 'in_app'"));

        factory.Clock.Advance(TimeSpan.FromDays(3));
        var second = await AlertTestSupport.AddEmployee(factory);
        await AddDriverDocumentsExcept(second, DocumentType.Medical);
        await AlertTestSupport.Apply(factory, "employee", second);
        Assert.Equal(1L, await Count($"select count(*) from fleetops.notifications where employee_id = '{colleague}' and template_code = 'critical_alert' and channel = 'in_app'"));
        Assert.Equal(2L, await Count($"select count(*) from fleetops.notifications where employee_id = '{away}' and template_code = 'critical_alert' and channel = 'in_app'"));
    }

    private async Task<Guid[]> DriverAlertNotifications(Guid recipient, Guid driver, string channel)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select notification.id from fleetops.notifications notification join fleetops.alerts alert on alert.id = notification.alert_id where notification.employee_id = $1 and alert.subject_id = $2 and notification.channel = $3", connection);
        command.Parameters.AddWithValue(recipient);
        command.Parameters.AddWithValue(driver);
        command.Parameters.AddWithValue(channel);
        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            ids.Add(reader.GetGuid(0));
        return ids.ToArray();
    }

    private WebApplicationFactory<Program> WithPush() =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Push:VapidPublicKey", PublicKey);
            builder.UseSetting("Push:VapidPrivateKey", PrivateKey);
        });

    private async Task<Guid> OfficePerson(string roles, string language)
    {
        var id = await AlertTestSupport.AddEmployee(factory, roles);
        await Execute($"update fleetops.employees set language_pref = '{language}' where id = '{id}'");
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OfficeUser>>();
        var result = await users.CreateAsync(new OfficeUser { Id = Guid.NewGuid(), EmployeeId = id, UserName = $"{id}@alert.test", Email = $"{id}@alert.test" });
        Assert.True(result.Succeeded);
        return id;
    }

    private async Task Subscribe(WebApplicationFactory<Program> host, Guid employeeId)
    {
        await Execute($"insert into fleetops.push_subscriptions (id, employee_id, endpoint, p256dh, auth) values ('{Guid.NewGuid()}', '{employeeId}', 'https://push.example.test/{employeeId}', 'key', 'auth')");
        using var scope = host.Services.CreateScope();
        Assert.True(scope.ServiceProvider.GetRequiredService<IPushConfiguration>().Get().Enabled);
    }

    private async Task AddDriverDocumentsExcept(Guid driver, DocumentType missing)
    {
        foreach (var type in new[] { DocumentType.LicenceC_CE, DocumentType.DriverCard, DocumentType.Code95, DocumentType.Medical }.Where(type => type != missing))
            await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, driver, type, new DateOnly(2028, 1, 1));
    }

    private async Task DeactivateRole(string role, Guid? except = null)
    {
        var keep = except is Guid id ? $" and id <> '{id}'" : "";
        await Execute($"update fleetops.employees set status = 'Left' where roles like '%\"{role}\"%'{keep}");
    }

    private static async Task RunJob(WebApplicationFactory<Program> host, string name)
    {
        using var scope = host.Services.CreateScope();
        var job = scope.ServiceProvider.GetRequiredService<IEnumerable<IJob>>().Single(candidate => candidate.Name == name);
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        string? cursor = null;
        JobStep step;
        do
        {
            step = await job.RunStepAsync(new JobContext(cursor, clock.GetUtcNow(), new SystemActor("test")), CancellationToken.None);
            cursor = step.Cursor;
        } while (!step.Done);
    }

    private static IReadOnlyCollection<EmailMessage> Emails(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<DemoEmailSender>().Inbox;

    private static IReadOnlyCollection<PushMessage> Pushes(WebApplicationFactory<Program> host) =>
        host.Services.GetRequiredService<DemoPushSender>().Recorded;

    private async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<object?> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(factory.AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task<long> Count(string sql) => Convert.ToInt64(await Scalar(sql));

    private sealed class RecordingSms : IEnrolmentSms
    {
        public List<(string Phone, string Message)> Sent { get; } = [];

        public Task SendAsync(string phone, string code, CancellationToken cancellationToken)
        {
            Sent.Add((phone, code));
            return Task.CompletedTask;
        }
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
