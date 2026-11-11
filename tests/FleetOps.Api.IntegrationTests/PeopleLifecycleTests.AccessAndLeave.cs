using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public sealed partial class PeopleLifecycleTests
{
    private async Task<Shift> Shift(Guid employeeId, ShiftStatus status = ShiftStatus.Running)
    {
        await using var db = Database();
        var shift = ShiftRules.Start(Guid.NewGuid(), employeeId, Today, Start, null);
        if (status != ShiftStatus.Running)
        {
            ShiftRules.End(shift, Start.AddHours(2), null);
        }
        shift.Status = status;
        shift.SubmittedAt = status == ShiftStatus.Submitted ? Start.AddHours(2) : null;
        shift.DeviceTime = Start;
        shift.ServerReceivedAt = Start;
        db.Shifts.Add(shift);
        await db.SaveChangesAsync();
        return shift;
    }

    private async Task<(Guid Assignment, Guid FutureAssignment, Guid Cycle, Guid FutureCycle, Guid Alert)> Plans(Guid employeeId)
    {
        using var client = factory.AsDispatcher();
        var truck = await factory.CreateTruck(client);
        await using var db = Database();
        var assignment = new Assignment { EmployeeId = employeeId, VehicleId = truck.Id, From = Today.AddDays(-3) };
        var futureAssignment = new Assignment { EmployeeId = employeeId, VehicleId = truck.Id, From = Today.AddDays(3) };
        var cycle = new Cycle { Source = CycleSource.Manual, EmployeeId = employeeId, VehicleId = truck.Id, From = Today.AddDays(-3), To = Today.AddDays(3) };
        var futureCycle = new Cycle { Source = CycleSource.Manual, EmployeeId = employeeId, VehicleId = truck.Id, From = Today.AddDays(4), To = Today.AddDays(8) };
        db.Assignments.AddRange(assignment, futureAssignment);
        db.Cycles.AddRange(cycle, futureCycle);
        await db.SaveChangesAsync();
        var alert = await AlertTestSupport.AddAlert(factory, "A-02", "employee", employeeId, "medical", "Critical", Today, "hr");
        return (assignment.Id, futureAssignment.Id, cycle.Id, futureCycle.Id, alert);
    }

    private async Task<Guid> SeedSession(Guid employeeId)
    {
        await using var db = Database();
        var user = new OfficeUser { Id = Guid.NewGuid(), EmployeeId = employeeId, UserName = $"{employeeId}@example.test" };
        db.Users.Add(user);
        var session = new OfficeSession
        {
            Id = Guid.NewGuid(), UserId = user.Id, EmployeeId = employeeId, TokenHash = SessionTokens.Hash(SessionTokens.New()),
            Device = "driver", Stage = SessionStage.Full, CreatedAt = Start, StageChangedAt = Start, LastSeenAt = Start
        };
        db.OfficeSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    [Fact]
    public async Task Leave_preview_lists_sessions_assignments_cycles_alerts_and_running_shift()
    {
        var id = await Employee();
        var session = await SeedSession(id);
        var plans = await Plans(id);
        var shift = await Shift(id);
        using var client = factory.AsHr();
        var body = await Body(await client.GetAsync($"/api/v1/people/{id}/leave-preview?endDate=2026-11-06"));
        Assert.Contains(session, body.GetProperty("sessionsToRevoke").EnumerateArray().Select(item => item.GetGuid()));
        Assert.Contains(plans.Assignment, body.GetProperty("assignmentsToEnd").EnumerateArray().Select(item => item.GetGuid()));
        Assert.Contains(plans.FutureAssignment, body.GetProperty("assignmentsToRemove").EnumerateArray().Select(item => item.GetGuid()));
        Assert.Contains(plans.Cycle, body.GetProperty("cyclesToEnd").EnumerateArray().Select(item => item.GetGuid()));
        Assert.Contains(plans.FutureCycle, body.GetProperty("cyclesToRemove").EnumerateArray().Select(item => item.GetGuid()));
        Assert.Contains(plans.Alert, body.GetProperty("alertsToResolve").EnumerateArray().Select(item => item.GetGuid()));
        Assert.Equal(shift.Id, body.GetProperty("runningShiftId").GetGuid());
        Assert.Contains(shift.Id, body.GetProperty("shiftsToApprove").EnumerateArray().Select(item => item.GetGuid()));
        await using var db = Database();
        Assert.Null((await db.OfficeSessions.SingleAsync()).RevokedAt);
        Assert.Equal(2, await db.Assignments.CountAsync());
        Assert.Equal(2, await db.Cycles.CountAsync());
        Assert.Equal(ShiftStatus.Running, (await db.Shifts.SingleAsync()).Status);
        Assert.Equal(AlertStatus.Open, (await db.Alerts.SingleAsync(alert => alert.Id == plans.Alert)).Status);
    }

    [Fact]
    public async Task Leave_with_a_running_shift_needs_an_end_time_and_reason()
    {
        var id = await Employee();
        var shift = await Shift(id);
        using var client = factory.AsHr();
        foreach (var input in new object[]
        {
            new { endDate = Today, reason = "Contract ended" },
            new { endDate = Today, reason = "Contract ended", runningShiftEnd = Start.AddHours(2) },
            new { endDate = Today, reason = "Contract ended", runningShiftReason = "Paper record" }
        })
        {
            await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/leave", input), HttpStatusCode.Conflict, "running_shift_needs_end");
            await AssertStatus(id, EmployeeStatus.Active);
            await using var unchanged = Database();
            Assert.Null((await unchanged.Shifts.SingleAsync()).EndAt);
        }
        await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/leave", new { endDate = Today, reason = "Contract ended", runningShiftEnd = Start.AddHours(2), runningShiftReason = "Paper record" }));
        await using var db = Database();
        Assert.Equal(Start.AddHours(2), (await db.Shifts.SingleAsync()).EndAt);
        Assert.Contains(await db.AuditEntries.ToArrayAsync(), entry => entry.EntityId == shift.Id && entry.Reason == "Paper record");
        await AssertStatus(id, EmployeeStatus.Left);
    }

    [Fact]
    public async Task Employee_left_revokes_sessions_ends_plans_and_resolves_alerts()
    {
        var id = await Employee();
        var session = await SeedSession(id);
        var plans = await Plans(id);
        using var client = factory.AsHr();
        var body = await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/leave", new { endDate = Today, reason = "Contract ended" }));
        Assert.Equal("left", body.GetProperty("status").GetString());
        await using var db = Database();
        var employee = await db.Employees.SingleAsync(employee => employee.Id == id);
        Assert.Equal(EmployeeStatus.Left, employee.Status);
        Assert.Equal(Today, employee.EndDate);
        Assert.NotNull((await db.OfficeSessions.SingleAsync(row => row.Id == session)).RevokedAt);
        Assert.Equal(Today, (await db.Assignments.SingleAsync(row => row.Id == plans.Assignment)).To);
        Assert.False(await db.Assignments.AnyAsync(row => row.Id == plans.FutureAssignment));
        Assert.Equal(Today, (await db.Cycles.SingleAsync(row => row.Id == plans.Cycle)).To);
        Assert.False(await db.Cycles.AnyAsync(row => row.Id == plans.FutureCycle));
        Assert.Equal(AlertStatus.Resolved, (await db.Alerts.SingleAsync(row => row.Id == plans.Alert)).Status);
        Assert.Contains(await db.AuditEntries.ToArrayAsync(), entry => entry.EntityId == plans.Alert && entry.Reason == "Person left" && entry.ActorId == "system");
    }

    [Fact]
    public async Task Shifts_of_a_leaver_can_still_be_approved()
    {
        var id = await Employee();
        var shift = await Shift(id, ShiftStatus.Submitted);
        using var hr = factory.AsHr();
        await Body(await hr.PostAsJsonAsync($"/api/v1/people/{id}/leave", new { endDate = Today, reason = "Contract ended" }));
        using var dispatcher = factory.AsDispatcher();
        var body = await Body(await dispatcher.PostAsJsonAsync("/api/v1/timesheets/approve", new { items = new[] { new { shiftId = shift.Id, version = shift.Version, note = "Final shift checked" } } }));
        var result = Assert.Single(body.GetProperty("results").EnumerateArray());
        Assert.Equal("approved", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("code").ValueKind);
        await using var db = Database();
        Assert.Equal(ShiftStatus.Approved, (await db.Shifts.SingleAsync()).Status);
        await AssertStatus(id, EmployeeStatus.Left);
    }

    [Fact]
    public async Task Scheduled_leave_applies_on_the_end_date_in_the_sweep()
    {
        var id = await Employee();
        var session = await SeedSession(id);
        using var client = factory.AsHr();
        await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/leave", new { endDate = Today.AddDays(1), reason = "Scheduled departure" }));
        await AlertTestSupport.Sweep(factory);
        await AssertStatus(id, EmployeeStatus.Active);
        await using (var before = Database())
        {
            Assert.Null((await before.OfficeSessions.SingleAsync(row => row.Id == session)).RevokedAt);
        }
        factory.Clock.Advance(TimeSpan.FromDays(1));
        await AlertTestSupport.Sweep(factory);
        await AssertStatus(id, EmployeeStatus.Left);
        await using var after = Database();
        Assert.NotNull((await after.OfficeSessions.SingleAsync(row => row.Id == session)).RevokedAt);
        Assert.Equal(Today.AddDays(1), (await after.Employees.SingleAsync(employee => employee.Id == id)).EndDate);
        var profile = await Body(await client.GetAsync($"/api/v1/people/{id}"));
        Assert.Equal("left", profile.GetProperty("header").GetProperty("status").GetString());
    }

    private static object StartBatch(Guid? shiftId = null) => new
    {
        commands = new[] { new { commandId = Guid.NewGuid(), type = "shift.start", schemaVersion = 1, aggregateId = shiftId ?? Guid.NewGuid(), baseVersion = 0, deviceTime = Start, payload = new { workWithoutVehicle = true } } }
    };

    [Fact]
    public async Task Suspended_driver_keeps_the_session_but_cannot_start_a_shift()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        using var driver = await scenario.FullDriver();
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Post, $"/api/v1/people/{scenario.Driver.EmployeeId}/suspend", new { reason = "Safety review" }));
        var config = await Body(await scenario.Factory.Send(driver, HttpMethod.Get, "/api/v1/driver/config"));
        Assert.Equal("suspended", config.GetProperty("employeeStatus").GetString());
        var batch = await Body(await scenario.Factory.Send(driver, HttpMethod.Post, "/api/v1/driver/commands/batch", StartBatch()));
        var result = Assert.Single(batch.GetProperty("results").EnumerateArray());
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal("account_suspended", result.GetProperty("code").GetString());
        Assert.Null(Assert.Single(await scenario.Sessions()).RevokedAt);
        await scenario.Database(async db => Assert.False(await db.Shifts.AnyAsync(shift => shift.EmployeeId == scenario.Driver.EmployeeId)));
    }

    [Fact]
    public async Task Revoked_session_is_refused_at_once_with_session_revoked()
    {
        await using var scenario = await DriverEnrolmentScenario.Create("dispatcher");
        using var driver = await scenario.FullDriver();
        var session = Assert.Single(await scenario.Sessions());
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Post, $"/api/v1/people/{scenario.Driver.EmployeeId}/sessions/{session.Id}/revoke", new { reason = "Lost phone" }), HttpStatusCode.OK, "session_revoked");
        await Body(await scenario.Factory.Send(driver, HttpMethod.Post, "/api/v1/driver/commands/batch", StartBatch()), HttpStatusCode.Unauthorized, "session_revoked");
        var stored = Assert.Single(await scenario.Sessions());
        Assert.NotNull(stored.RevokedAt);
        Assert.Equal("Lost phone", stored.RevokeReason);
        await scenario.Database(async db => Assert.False(await db.Shifts.AnyAsync(shift => shift.EmployeeId == scenario.Driver.EmployeeId)));
    }

    [Fact]
    public async Task Dispatcher_cannot_change_a_number_and_hr_change_revokes_and_tells_the_old_number()
    {
        await using var scenario = await DriverEnrolmentScenario.Create("dispatcher");
        using var driver = await scenario.FullDriver();
        var path = $"/api/v1/people/{scenario.Driver.EmployeeId}/phone";
        var input = new { phone = "+4915112345679", reason = "Replacement phone" };
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Put, path, input), HttpStatusCode.Forbidden, "permission_denied");
        await scenario.Database(async db =>
        {
            Assert.Equal("+4915112345678", (await db.Employees.SingleAsync(employee => employee.Id == scenario.Driver.EmployeeId)).Phone);
            (await db.Employees.SingleAsync(employee => employee.Id == scenario.Actor.EmployeeId)).Roles = "[\"hr\"]";
            await db.SaveChangesAsync();
        });
        var body = await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Put, path, input), HttpStatusCode.OK, "phone_changed");
        Assert.Equal("+4915112345678", body.GetProperty("testInbox").GetProperty("phone").GetString());
        Assert.Contains("phone number was changed", body.GetProperty("testInbox").GetProperty("message").GetString());
        var session = Assert.Single(await scenario.Sessions());
        Assert.NotNull(session.RevokedAt);
        Assert.Equal("number_changed", session.RevokeReason);
        await scenario.Database(async db => Assert.Equal(input.phone, (await db.Employees.SingleAsync(employee => employee.Id == scenario.Driver.EmployeeId)).Phone));
        await Body(await scenario.Factory.Send(driver, HttpMethod.Get, "/api/v1/driver/config"), HttpStatusCode.Unauthorized, "session_revoked");
    }

    [Fact]
    public async Task Pin_reset_sets_the_flag_and_the_driver_clears_it()
    {
        await using var scenario = await DriverEnrolmentScenario.Create("dispatcher");
        using var driver = await scenario.FullDriver();
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Post, $"/api/v1/people/{scenario.Driver.EmployeeId}/pin-reset"), HttpStatusCode.OK, "pin_reset_requested");
        Assert.NotNull(Assert.Single(await scenario.Sessions()).PinResetRequestedAt);
        var config = await Body(await scenario.Factory.Send(driver, HttpMethod.Get, "/api/v1/driver/config"));
        Assert.True(config.GetProperty("pinResetRequired").GetBoolean());
        await Body(await scenario.Factory.Send(driver, HttpMethod.Post, "/api/v1/driver/pin-reset/complete"), HttpStatusCode.OK, "pin_reset_completed");
        Assert.Null(Assert.Single(await scenario.Sessions()).PinResetRequestedAt);
        var cleared = await Body(await scenario.Factory.Send(driver, HttpMethod.Get, "/api/v1/driver/config"));
        Assert.False(cleared.GetProperty("pinResetRequired").GetBoolean());
    }

    [Fact]
    public async Task Role_change_needs_step_up_and_is_audited()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        await scenario.Database(async db =>
        {
            (await db.OfficeSessions.SingleAsync(session => session.EmployeeId == scenario.Actor.EmployeeId)).StepUpAt = scenario.Factory.Clock.GetUtcNow().AddMinutes(-11);
            await db.SaveChangesAsync();
        });
        var path = $"/api/v1/people/{scenario.Driver.EmployeeId}/roles";
        var input = new { roles = new[] { "driver", "dispatcher" }, reason = "Planning duties" };
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Put, path, input), HttpStatusCode.Forbidden, "step_up_required");
        await scenario.Database(async db => Assert.Equal("[\"driver\"]", (await db.Employees.SingleAsync(employee => employee.Id == scenario.Driver.EmployeeId)).Roles));
        using (var scope = scenario.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<OfficeUser>>();
            var current = await users.FindByIdAsync(scenario.Actor.Id.ToString());
            Assert.NotNull(current);
            Assert.True((await users.ResetAuthenticatorKeyAsync(current)).Succeeded);
        }
        var code = await scenario.Factory.CurrentTotp(scenario.Actor);
        using var stepUp = await scenario.Factory.Send(scenario.Office, HttpMethod.Post, "/api/v1/auth/step-up", new { code });
        Assert.Equal(HttpStatusCode.OK, stepUp.StatusCode);
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Put, path, input), HttpStatusCode.OK, "roles_updated");
        await scenario.Database(async db =>
        {
            var employee = await db.Employees.SingleAsync(employee => employee.Id == scenario.Driver.EmployeeId);
            Assert.Equal(input.roles, JsonSerializer.Deserialize<string[]>(employee.Roles));
            Assert.Contains(await db.AuditEntries.Where(entry => entry.EntityId == employee.Id).ToArrayAsync(), entry => entry.FieldName == "roles" && entry.Reason == input.reason && entry.ActorId == scenario.Actor.EmployeeId.ToString());
        });
    }

    [Fact]
    public async Task Only_an_owner_grants_owner_and_the_last_owner_stays()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        var path = $"/api/v1/people/{scenario.Driver.EmployeeId}/roles";
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Put, path, new { roles = new[] { "driver", "owner" }, reason = "Ownership" }), HttpStatusCode.Forbidden, "permission_denied");
        await scenario.Database(async db =>
        {
            Assert.DoesNotContain("owner", (await db.Employees.SingleAsync(employee => employee.Id == scenario.Driver.EmployeeId)).Roles);
            (await db.Employees.SingleAsync(employee => employee.Id == scenario.Actor.EmployeeId)).Roles = "[\"owner\"]";
            await db.SaveChangesAsync();
        });
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Put, $"/api/v1/people/{scenario.Actor.EmployeeId}/roles", new { roles = new[] { "hr" }, reason = "Transfer" }), HttpStatusCode.Conflict, "last_owner");
        await scenario.Database(async db => Assert.Equal("[\"owner\"]", (await db.Employees.SingleAsync(employee => employee.Id == scenario.Actor.EmployeeId)).Roles));
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Put, path, new { roles = new[] { "driver", "owner" }, reason = "Ownership" }), HttpStatusCode.OK, "roles_updated");
        await scenario.Database(async db => Assert.Contains("owner", JsonSerializer.Deserialize<string[]>((await db.Employees.SingleAsync(employee => employee.Id == scenario.Driver.EmployeeId)).Roles)!));
    }

    [Fact]
    public async Task Removing_all_office_roles_revokes_office_sessions()
    {
        await using var scenario = await DriverEnrolmentScenario.Create();
        await scenario.EditDriver(employee => employee.Roles = "[\"driver\",\"dispatcher\"]");
        await scenario.AddSession(scenario.Driver, "office");
        await scenario.AddSession(scenario.Driver, "driver");
        await Body(await scenario.Factory.Send(scenario.Office, HttpMethod.Put, $"/api/v1/people/{scenario.Driver.EmployeeId}/roles", new { roles = new[] { "driver" }, reason = "Driving only" }), HttpStatusCode.OK, "roles_updated");
        var sessions = await scenario.Sessions();
        var office = Assert.Single(sessions, session => session.Device == "office");
        Assert.NotNull(office.RevokedAt);
        Assert.Equal("office_roles_removed", office.RevokeReason);
        Assert.Null(Assert.Single(sessions, session => session.Device == "driver").RevokedAt);
        await scenario.Database(async db => Assert.Equal("[\"driver\"]", (await db.Employees.SingleAsync(employee => employee.Id == scenario.Driver.EmployeeId)).Roles));
    }
}



