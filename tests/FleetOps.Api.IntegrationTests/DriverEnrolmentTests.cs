using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public sealed class DriverEnrolmentTests
{
    [Fact]
    public async Task Hr_can_send_enrolment()
    {
        await using var scenario = await DriverEnrolmentScenario.Create("hr");
        await AssertSend(scenario);
    }

    [Fact]
    public async Task Dispatcher_can_send_enrolment()
    {
        await using var scenario = await DriverEnrolmentScenario.Create("dispatcher");
        await AssertSend(scenario);
    }

    [Fact]
    public async Task Owner_can_send_enrolment()
    {
        await using var scenario = await DriverEnrolmentScenario.Create("owner");
        await AssertSend(scenario);
    }

    [Fact]
    public async Task Driver_cannot_send_enrolment()
    {
        await using var scenario = await DriverEnrolmentScenario.Create("driver");
        using var response = await scenario.Send();
        await Refused(response, HttpStatusCode.Forbidden, "permission_denied");
        Assert.Empty(await scenario.Codes());
    }

    [Fact]
    public async Task Enrolment_needs_a_driver_with_number_and_phone()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        foreach (var missing in new[] { "role", "phone", "number" })
        {
            await scenario.EditDriver(employee =>
            {
                employee.Roles = missing == "role" ? "[\"hr\"]" : "[\"driver\"]";
                employee.Phone = missing == "phone" ? null : "+4915112345678";
                employee.PersonnelNumber = missing == "number" ? null : scenario.Number;
            });
            using var response = await scenario.Send();
            await Refused(response, HttpStatusCode.Conflict, "enrolment_not_possible");
            Assert.Empty(await scenario.Codes());
        }
    }

    [Fact]
    public async Task Code_is_stored_only_as_a_hash()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var code = await scenario.Issue();
        var row = Assert.Single(await scenario.Codes());
        Assert.DoesNotContain(code, row.CodeHash);
        Assert.Equal(SessionTokens.Hash($"{scenario.Driver.EmployeeId}:{code}"), row.CodeHash);
        await scenario.Database(async db =>
        {
            var audit = Assert.Single(await db.AuditEntries.Where(entry => entry.EntityId == scenario.Driver.EmployeeId).ToListAsync());
            Assert.Equal("enrolment", audit.FieldName);
            Assert.Equal("sent", audit.NewValue);
            Assert.DoesNotContain(code, JsonSerializer.Serialize(audit));
        });
    }

    [Fact]
    public async Task Test_inbox_is_off_by_default()
    {
        Assert.False(new EnrolmentOptions().TestInbox);
        await using var scenario = await DriverEnrolmentScenario.Create(testInbox: false);
        using var response = await scenario.Send();
        await Refused(response, HttpStatusCode.ServiceUnavailable, "sms_unavailable");
        Assert.Empty(await scenario.Codes());
        await scenario.Database(async db => Assert.Empty(await db.AuditEntries.Where(entry => entry.EntityId == scenario.Driver.EmployeeId).ToListAsync()));
    }

    [Fact]
    public async Task Sending_again_kills_the_old_code_and_revokes_sessions()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var sessionCode = await scenario.Issue();
        using var client = scenario.Client();
        using var enrolled = await scenario.Enrol(sessionCode, client);
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        var oldCode = await scenario.Issue();
        var oldRow = (await scenario.Codes()).Single(row => row.UsedAt == null);
        // A second live session makes revocation on this specific send observable.
        await scenario.AddSession(scenario.Driver, "driver");
        var newCode = await scenario.Issue();
        var rows = await scenario.Codes();
        Assert.NotNull(rows.Single(row => row.Id == oldRow.Id).UsedAt);
        Assert.Single(rows.Where(row => row.UsedAt == null));
        Assert.All(await scenario.Sessions(), session => Assert.NotNull(session.RevokedAt));
        using var refused = await scenario.Enrol(oldCode);
        await Refused(refused, HttpStatusCode.Unauthorized, "enrolment_failed");
        using var accepted = await scenario.Enrol(newCode);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Single((await scenario.Sessions()).Where(session => session.RevokedAt == null));
    }

    [Fact]
    public async Task Right_code_and_number_starts_a_session()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        using var response = await scenario.Enrol(await scenario.Issue());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie => cookie.StartsWith(AuthEndpoints.CookieName + "="));
        Assert.Equal("pending_privacy", (await Json(response)).GetProperty("stage").GetString());
        var session = Assert.Single(await scenario.Sessions());
        Assert.Equal(SessionStage.PendingPrivacy, session.Stage);
        Assert.Equal("driver", session.Device);
        Assert.NotNull(Assert.Single(await scenario.Codes()).UsedAt);
    }

    [Fact]
    public async Task Code_works_only_once()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var code = await scenario.Issue();
        using var first = await scenario.Enrol(code);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var second = await scenario.Enrol(code);
        await Refused(second, HttpStatusCode.Unauthorized, "enrolment_failed");
        Assert.Single(await scenario.Sessions());
        Assert.NotNull(Assert.Single(await scenario.Codes()).UsedAt);
    }

    [Fact]
    public async Task Expired_code_is_refused()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var code = await scenario.Issue();
        var row = Assert.Single(await scenario.Codes());
        scenario.Factory.Clock.SetUtcNow(row.ExpiresAt.AddSeconds(1));
        using var response = await scenario.Enrol(code);
        await Refused(response, HttpStatusCode.Unauthorized, "enrolment_failed");
        Assert.Empty(await scenario.Sessions());
        Assert.Null(Assert.Single(await scenario.Codes()).UsedAt);
    }

    [Fact]
    public async Task Failures_look_the_same()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var code = await scenario.Issue();
        using var unknown = await scenario.Enrol(code, number: "unknown-number");
        using var wrong = await scenario.Enrol(Wrong(code));
        using var success = await scenario.Enrol(code);
        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        using var used = await scenario.Enrol(code);
        var expiredCode = await scenario.Issue();
        scenario.Factory.Clock.SetUtcNow((await scenario.Codes()).Single(row => row.UsedAt == null).ExpiresAt.AddSeconds(1));
        using var expired = await scenario.Enrol(expiredCode);
        var expected = await unknown.Content.ReadAsByteArrayAsync();
        foreach (var response in new[] { unknown, wrong, used, expired })
        {
            await Refused(response, HttpStatusCode.Unauthorized, "enrolment_failed");
            Assert.Equal(expected, await response.Content.ReadAsByteArrayAsync());
        }
        Assert.Single(await scenario.Sessions());
        Assert.Null((await scenario.Codes()).Single(row => row.CodeHash == SessionTokens.Hash($"{scenario.Driver.EmployeeId}:{expiredCode}")).UsedAt);
    }

    [Fact]
    public async Task Wrong_code_uses_a_try_and_five_kill_it()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var code = await scenario.Issue();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var response = await scenario.Enrol(Wrong(code));
            await Refused(response, HttpStatusCode.Unauthorized, "enrolment_failed");
            Assert.Equal(attempt, Assert.Single(await scenario.Codes()).Attempts);
        }
        using var right = await scenario.Enrol(code);
        await Refused(right, HttpStatusCode.Unauthorized, "enrolment_failed");
        Assert.Empty(await scenario.Sessions());
        Assert.Null(Assert.Single(await scenario.Codes()).UsedAt);
    }

    [Fact]
    public async Task Wrong_number_with_a_right_code_uses_a_try()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var code = await scenario.Issue();
        using var response = await scenario.Enrol(code, number: "another-number");
        await Refused(response, HttpStatusCode.Unauthorized, "enrolment_failed");
        Assert.Equal(1, Assert.Single(await scenario.Codes()).Attempts);
        Assert.Empty(await scenario.Sessions());
        using var right = await scenario.Enrol(code);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Single(await scenario.Sessions());
    }

    [Fact]
    public async Task Suspended_driver_cannot_enrol()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var code = await scenario.Issue();
        await scenario.EditDriver(employee => employee.Status = EmployeeStatus.Suspended);
        using var response = await scenario.Enrol(code);
        await Refused(response, HttpStatusCode.Unauthorized, "enrolment_failed");
        Assert.Empty(await scenario.Sessions());
        Assert.Null(Assert.Single(await scenario.Codes()).UsedAt);
    }

    [Fact]
    public async Task Parallel_enrols_use_the_code_once()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var code = await scenario.Issue();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = Enumerable.Range(0, 8).Select(async _ =>
        {
            await start.Task;
            return await scenario.Enrol(code);
        }).ToArray();
        start.SetResult();
        var responses = await Task.WhenAll(requests);
        Assert.Single(responses.Where(response => response.StatusCode == HttpStatusCode.OK));
        foreach (var response in responses)
        {
            if (response.StatusCode != HttpStatusCode.OK)
                await Refused(response, HttpStatusCode.Unauthorized, "enrolment_failed");
            response.Dispose();
        }
        Assert.Single(await scenario.Sessions());
        Assert.NotNull(Assert.Single(await scenario.Codes()).UsedAt);
    }

    [Fact]
    public async Task Driver_session_survives_past_the_office_idle_limit()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        using var client = await scenario.FullDriver();
        var session = Assert.Single(await scenario.Sessions());
        scenario.Factory.Clock.Advance(TimeSpan.FromHours(1));
        using var response = await client.GetAsync("/api/v1/driver/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = Assert.Single(await scenario.Sessions());
        Assert.Equal(session.Id, current.Id);
        Assert.Null(current.RevokedAt);
        Assert.Equal(scenario.Factory.Clock.GetUtcNow(), current.LastSeenAt);
        using var office = await scenario.Factory.Send(scenario.Office, HttpMethod.Get, "/api/v1/people/lookup");
        await Refused(office, HttpStatusCode.Unauthorized, "session_expired");
    }

    [Fact]
    public async Task Driver_session_ends_when_employee_is_suspended()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        using var client = await scenario.FullDriver();
        using var before = await client.GetAsync("/api/v1/driver/config");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        await scenario.EditDriver(employee => employee.Status = EmployeeStatus.Suspended);
        using var after = await client.GetAsync("/api/v1/driver/config");
        await Refused(after, HttpStatusCode.Unauthorized, "session_expired");
        Assert.NotNull(Assert.Single(await scenario.Sessions()).RevokedAt);
    }

    [Fact]
    public async Task Driver_cannot_use_the_office_sign_in()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        using var client = scenario.Client();
        // CreateEmployee supplies a valid Identity password, so this tests the role restriction.
        using var response = await scenario.Factory.SignIn(client, scenario.Driver);
        await Refused(response, HttpStatusCode.Unauthorized, "sign_in_failed");
        Assert.Empty(await scenario.Sessions());
    }

    [Fact]
    public async Task Privacy_notice_opens_a_driver_session()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        using var client = scenario.Client();
        using var enrolled = await scenario.Enrol(await scenario.Issue(), client);
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        Assert.Equal(SessionStage.PendingPrivacy, Assert.Single(await scenario.Sessions()).Stage);
        using var response = await scenario.Privacy(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("done", (await Json(response)).GetProperty("next").GetString());
        Assert.Equal(SessionStage.Full, Assert.Single(await scenario.Sessions()).Stage);
        await scenario.Database(async db =>
        {
            var acknowledgement = Assert.Single(await db.PrivacyAcknowledgements.Where(row => row.EmployeeId == scenario.Driver.EmployeeId).ToListAsync());
            Assert.Equal(AuthOptions.PrivacyVersion, acknowledgement.Version);
        });
        using var config = await client.GetAsync("/api/v1/driver/config");
        Assert.Equal(HttpStatusCode.OK, config.StatusCode);
    }

    [Fact]
    public async Task Driver_config_returns_settings()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        await scenario.Database(async db =>
        {
            foreach (var pair in new Dictionary<string, int> { ["driver.pin_length"] = 7, ["driver.max_pin_tries"] = 9, ["driver.max_offline_days"] = 11 })
            {
                var setting = await db.Settings.FindAsync(pair.Key);
                if (setting is null)
                    db.Settings.Add(new Setting { Key = pair.Key, Value = pair.Value, UpdatedAt = scenario.Factory.Clock.GetUtcNow(), UpdatedBy = "test" });
                else
                    setting.Value = pair.Value;
            }
            await db.SaveChangesAsync();
        });
        using var client = await scenario.FullDriver();
        using var response = await client.GetAsync("/api/v1/driver/config");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var config = await Json(response);
        Assert.Equal(7, config.GetProperty("pinLength").GetInt32());
        Assert.Equal(9, config.GetProperty("maxPinTries").GetInt32());
        Assert.Equal(11, config.GetProperty("maxOfflineDays").GetInt32());
        Assert.Equal(DriverEnrolmentScenario.OfficePhone, config.GetProperty("officePhone").GetString());
        Assert.Equal("Test", config.GetProperty("name").GetString());
        Assert.Null(Assert.Single(await scenario.Sessions()).RevokedAt);
    }

    [Fact]
    public async Task Office_user_cannot_read_driver_config()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        using var response = await scenario.Office.GetAsync("/api/v1/driver/config");
        await Refused(response, HttpStatusCode.Forbidden, "permission_denied");
        await scenario.Database(async db => Assert.Equal(SessionStage.Full, (await db.OfficeSessions.SingleAsync(row => row.EmployeeId == scenario.Actor.EmployeeId)).Stage));
    }

    [Fact]
    public async Task Driver_cannot_read_people()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        using var client = await scenario.FullDriver();
        using var response = await client.GetAsync("/api/v1/people/lookup");
        await Refused(response, HttpStatusCode.Forbidden, "permission_denied");
        Assert.Equal(SessionStage.Full, Assert.Single(await scenario.Sessions()).Stage);
    }

    private static async Task AssertSend(DriverEnrolmentScenario scenario)
    {
        var code = await scenario.Issue();
        Assert.Matches("^[0-9]{6}$", code);
        var row = Assert.Single(await scenario.Codes());
        Assert.Equal(scenario.Actor.EmployeeId, row.IssuedBy);
        Assert.Equal(scenario.Driver.EmployeeId, row.EmployeeId);
        Assert.Equal(0, row.Attempts);
        Assert.Null(row.UsedAt);
        Assert.True(row.ExpiresAt > row.CreatedAt);
    }

    private static string Wrong(string code) => code == "000000" ? "000001" : "000000";

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task Refused(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await Json(response)).GetProperty("code").GetString());
    }
}
