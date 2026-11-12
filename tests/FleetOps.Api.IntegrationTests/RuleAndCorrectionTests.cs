using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Setup;
using FleetOps.Modules.Shifts;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FleetOps.Tests;

public sealed class RuleAndCorrectionTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private Guid employeeId;
    private static readonly DateOnly Day = new(2026, 11, 2);
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        employeeId = await AlertTestSupport.AddEmployee(factory);
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Rule_values_come_from_rows_not_constants()
    {
        var shift = await Seed();
        await SetBreak(30);
        Assert.Contains((await Snapshot()).Shifts.Single().RuleWarnings!, warning => warning.RuleId == "C-04");
        await SetBreak(120);
        Assert.DoesNotContain((await Snapshot()).Shifts.Single().RuleWarnings!, warning => warning.RuleId == "C-04");
        await using var database = Database();
        Assert.Contains("120", (await database.ComplianceRules.SingleAsync(rule => rule.Id == "C-04", Cancellation)).Value!);
        Assert.Equal(shift.Id, (await database.Shifts.SingleAsync(Cancellation)).Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rule_warnings_never_fail_a_shift_command(bool computationFails)
    {
        var shift = await Seed(ShiftStatus.Running);
        await SetBreak(30);
        if (computationFails)
        {
            await using var setup = Database();
            var rule = await setup.ComplianceRules.SingleAsync(row => row.Id == "C-04", Cancellation);
            rule.Value = "{}";
            await setup.SaveChangesAsync(Cancellation);
        }
        var result = await Batch(Command("shift.end", shift.Id, new { endAt = shift.StartAt.AddHours(1) }, 1));
        Assert.Equal("applied", result.Status);
        Assert.Null(result.Code);
        if (computationFails)
        {
            Assert.Empty(result.Shift!.RuleWarnings);
        }
        else
        {
            Assert.Contains(result.Shift!.RuleWarnings, warning => warning.RuleId == "C-04");
        }
        await using var database = Database();
        Assert.Equal(ShiftStatus.Draft, (await database.Shifts.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task Unconfirmed_rule_warnings_are_info_with_guide_wording()
    {
        await Seed();
        await SetBreak(30);
        var warning = Assert.Single((await Snapshot()).Shifts.Single().RuleWarnings!.Where(warning => warning.RuleId == "C-04"));
        Assert.Equal("info", warning.Severity);
        Assert.Equal("Guide value \u00b7 rule not yet confirmed", warning.Message);
        await using var database = Database();
        Assert.False((await database.ComplianceRules.SingleAsync(rule => rule.Id == "C-04", Cancellation)).Confirmed);
    }

    [Fact]
    public async Task Confirmed_rule_warning_says_possible_breach_never_violation()
    {
        await Seed();
        await SetBreak(30);
        using var owner = factory.AsOwner();
        using var response = await owner.PostAsync("/api/v1/settings/compliance-rules/C-04/confirm", null, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var warning = Assert.Single((await Snapshot()).Shifts.Single().RuleWarnings!.Where(warning => warning.RuleId == "C-04"));
        Assert.Equal("warning", warning.Severity);
        Assert.Equal("Possible breach \u2014 check against tachograph", warning.Message);
        Assert.DoesNotContain("violation", warning.Message, StringComparison.OrdinalIgnoreCase);
        await using var database = Database();
        Assert.True((await database.ComplianceRules.SingleAsync(rule => rule.Id == "C-04", Cancellation)).Confirmed);
        Assert.Single(await database.RuleConfirmations.Where(row => row.RuleId == "C-04").ToArrayAsync(Cancellation));
    }

    [Fact]
    public async Task Rule_cases_fixture_matches_the_server_engine()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "fixtures", "rule-cases.json")));
        foreach (var testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            await using (var database = Database())
            {
                await database.Shifts.ExecuteDeleteAsync(Cancellation);
                var rules = await database.ComplianceRules.ToArrayAsync(Cancellation);
                foreach (var rule in rules)
                {
                    rule.Value = null;
                }
                foreach (var input in testCase.GetProperty("rules").EnumerateArray())
                {
                    var rule = rules.Single(rule => rule.Id == input.GetProperty("id").GetString());
                    rule.Value = input.GetProperty("value").ValueKind == JsonValueKind.Null ? null : input.GetProperty("value").GetRawText();
                    rule.Confirmed = input.GetProperty("confirmed").GetBoolean();
                }
                var shift = NewShift();
                var cursor = shift.StartAt;
                shift.Activities.Clear();
                foreach (var input in testCase.GetProperty("activities").EnumerateArray())
                {
                    var end = cursor.AddMinutes(input.GetProperty("minutes").GetInt32());
                    var type = Enum.Parse<ActivityType>(input.GetProperty("type").GetString()!.Replace("_", ""), true);
                    shift.Activities.Add(new ShiftActivity { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = type, StartAt = cursor, EndAt = end });
                    cursor = end;
                }
                shift.EndAt = cursor;
                database.Shifts.Add(shift);
                await database.SaveChangesAsync(Cancellation);
            }
            var snapshot = await Snapshot();
            var warnings = testCase.TryGetProperty("scope", out var scope) && scope.GetString() == "week" ? snapshot.WeekGuidance.Single().RuleWarnings : snapshot.Shifts.Single().RuleWarnings!;
            Assert.Equal(testCase.GetProperty("expectedWarnings").EnumerateArray().Select(value => value.GetString()).Order(), warnings.Select(warning => warning.RuleId).Order());
        }
    }

    [Theory]
    [InlineData("dispatcher")]
    [InlineData("hr")]
    [InlineData("owner")]
    public async Task A11_acknowledge_needs_a_note_and_a_new_warning_reopens_it(string role)
    {
        var shift = await Seed(ShiftStatus.Running);
        await SetBreak(30);
        await Batch(Command("shift.end", shift.Id, new { endAt = shift.StartAt.AddHours(1) }, 1));
        Guid alertId;
        int version;
        await using (var database = Database())
        {
            var alert = await database.Alerts.SingleAsync(row => row.RuleId == "A-11", Cancellation);
            alertId = alert.Id;
            version = alert.Version;
            Assert.Equal(AlertSeverity.Info, alert.Severity);
        }
        using (var office = role == "hr" ? factory.AsHr() : role == "owner" ? factory.AsOwner() : factory.AsDispatcher())
        {
            using var refused = await office.PostAsJsonAsync($"/api/v1/alerts/{alertId}/acknowledge-rule-warning", new { version, note = " " }, Cancellation);
            await Error(refused, HttpStatusCode.BadRequest, "validation_error");
            using var accepted = await office.PostAsJsonAsync($"/api/v1/alerts/{alertId}/acknowledge-rule-warning", new { version, note = "Checked tachograph" }, Cancellation);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        await using (var database = Database())
        {
            Assert.Equal(AlertStatus.Resolved, (await database.Alerts.SingleAsync(row => row.Id == alertId, Cancellation)).Status);
            var rule = await database.ComplianceRules.SingleAsync(row => row.Id == "C-07", Cancellation);
            rule.Value = "{\"workMinutes\":30,\"breakMinutes\":30,\"longWorkMinutes\":60,\"longBreakMinutes\":45}";
            await database.SaveChangesAsync(Cancellation);
        }
        var result = await Batch(Command("shift.submit", shift.Id, new { }, 2));
        Assert.Equal("applied", result.Status);
        Assert.Null(result.Code);
        await using var verify = Database();
        Assert.Equal(AlertStatus.Open, (await verify.Alerts.SingleAsync(row => row.Id == alertId, Cancellation)).Status);
    }

    [Fact]
    public async Task Rows_without_a_value_cannot_be_confirmed()
    {
        using var owner = factory.AsOwner();
        using var edit = await owner.PutAsJsonAsync("/api/v1/settings/compliance-rules/C-04", new { value = (object?)null }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        using var response = await owner.PostAsync("/api/v1/settings/compliance-rules/C-04/confirm", null, Cancellation);
        await Error(response, HttpStatusCode.BadRequest, "validation_error");
        await using var database = Database();
        Assert.False((await database.ComplianceRules.SingleAsync(rule => rule.Id == "C-04", Cancellation)).Confirmed);
        Assert.Empty(await database.RuleConfirmations.ToArrayAsync(Cancellation));
    }

    [Fact]
    public async Task Correction_request_is_idempotent_and_opens_a14()
    {
        var shift = await Seed();
        var command = Request(shift);
        var first = await Batch(command);
        var replay = await Batch(command);
        Assert.Equal("applied", first.Status);
        Assert.Null(first.Code);
        Assert.Equal(first, replay);
        await using var database = Database();
        Assert.Equal(command.AggregateId, Assert.Single(await database.CorrectionRequests.ToArrayAsync(Cancellation)).Id);
        var alert = Assert.Single(await database.Alerts.Where(row => row.RuleId == "A-14").ToArrayAsync(Cancellation));
        Assert.Equal(command.AggregateId, alert.SubjectId);
        Assert.Equal("dispatcher", alert.AssignedRole);
        Assert.Equal(AlertStatus.Open, alert.Status);
    }

    [Fact]
    public async Task Missing_shift_request_carries_missing_shift()
    {
        using (var office = factory.AsDispatcher())
        {
            var truck = await factory.CreateTruck(office);
            await using var setup = Database();
            setup.Cycles.Add(new Cycle { EmployeeId = employeeId, VehicleId = truck.Id, From = Day, To = Day.AddDays(6), Status = CycleStatus.Active, Source = CycleSource.Manual });
            await setup.SaveChangesAsync(Cancellation);
        }
        var command = Command("correction.request", Guid.NewGuid(), new { shiftId = (Guid?)null, date = Day, reason = "missing_shift" });
        var result = await Batch(command);
        Assert.Equal("applied", result.Status);
        Assert.Null(result.Code);
        await using var database = Database();
        var request = Assert.Single(await database.CorrectionRequests.ToArrayAsync(Cancellation));
        Assert.Equal("missing_shift", request.Reason);
        Assert.Null(request.ShiftId);
        Assert.Equal("missing_shift", Assert.Single((await Snapshot()).Corrections!).Reason);
    }

    [Fact]
    public async Task Correction_on_an_exported_shift_goes_to_hr()
    {
        var shift = await Seed(ShiftStatus.Exported);
        var command = Request(shift);
        var result = await Batch(command);
        Assert.Equal("applied", result.Status);
        Assert.Null(result.Code);
        await using var database = Database();
        Assert.True((await database.CorrectionRequests.SingleAsync(Cancellation)).RequiresHr);
        Assert.Equal("hr", (await database.Alerts.SingleAsync(row => row.RuleId == "A-14", Cancellation)).AssignedRole);
        using var office = factory.AsDispatcher();
        using var refused = await office.PostAsJsonAsync($"/api/v1/corrections/{command.AggregateId}/decide", new { employeeId, decision = "decline", reason = "Reviewed" }, Cancellation);
        await Error(refused, HttpStatusCode.Forbidden, "permission_denied");
    }

    [Fact]
    public async Task Declined_correction_tells_the_driver_the_reason()
    {
        var shift = await Seed();
        var command = Request(shift);
        Assert.Equal("applied", (await Batch(command)).Status);
        await Decide(command.AggregateId, "decline", "Tachograph agrees with recorded times");
        var request = Assert.Single((await Snapshot()).Corrections!);
        Assert.Equal("declined", request.Status);
        Assert.Equal("Tachograph agrees with recorded times", request.DeclineReason);
        await using var database = Database();
        Assert.Equal(request.DeclineReason, (await database.CorrectionRequests.SingleAsync(Cancellation)).DeclineReason);
        Assert.Contains(await database.Notifications.ToArrayAsync(Cancellation), row => row.TemplateCode == "correction_declined");
    }

    [Fact]
    public async Task Done_correction_tells_the_driver_the_difference()
    {
        var shift = await Seed(activityType: ActivityType.OtherWork);
        var command = Request(shift);
        Assert.Equal("applied", (await Batch(command)).Status);
        using (var office = factory.AsDispatcher())
        {
            using var edit = await office.PutAsJsonAsync($"/api/v1/timesheets/shifts/{shift.Id}", new EditShiftRequest(1, "Correct recorded end", new(Day, shift.StartAt, shift.EndAt!.Value.AddHours(1), null, null, null, null, null, null)), VehicleApiFactory.JsonOptions, Cancellation);
            Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        }
        await Decide(command.AggregateId, "done");
        var request = Assert.Single((await Snapshot()).Corrections!);
        Assert.Equal("done", request.Status);
        var difference = Assert.Single(request.Diffs.Where(diff => diff.Field == "endAt"));
        Assert.Equal(shift.EndAt, DateTimeOffset.Parse(difference.Before!));
        Assert.Equal(shift.EndAt!.Value.AddHours(1), DateTimeOffset.Parse(difference.After!));
        await using var database = Database();
        Assert.Contains("endAt", (await database.CorrectionRequests.SingleAsync(Cancellation)).Diffs);
        Assert.Contains(await database.Notifications.ToArrayAsync(Cancellation), row => row.TemplateCode == "correction_done");
    }

    private async Task Decide(Guid id, string decision, string? reason = null)
    {
        using var office = factory.AsDispatcher();
        using var response = await office.PostAsJsonAsync($"/api/v1/corrections/{id}/decide", new { employeeId, decision, reason }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private DriverCommand Request(Shift shift) => Command("correction.request", Guid.NewGuid(), new { shiftId = shift.Id, date = shift.Date, reason = "wrong_times", note = "Check end time" });
    private DriverCommand Command(string type, Guid id, object payload, int version = 0) => new(Guid.NewGuid(), type, 1, id, version, factory.Clock.GetUtcNow(), JsonSerializer.SerializeToElement(payload));

    private async Task<DriverCommandResult> Batch(DriverCommand command)
    {
        using var driver = factory.AsDriver(employeeId);
        using var response = await driver.PostAsJsonAsync("/api/v1/driver/commands/batch", new DriverCommandBatch([command]), VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<DriverCommandBatchResult>(VehicleApiFactory.JsonOptions, Cancellation))!.Results);
    }

    private async Task<ShiftSnapshot> Snapshot()
    {
        using var driver = factory.AsDriver(employeeId);
        using var response = await driver.GetAsync("/api/v1/driver/shifts", Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ShiftSnapshot>(VehicleApiFactory.JsonOptions, Cancellation))!;
    }

    private async Task SetBreak(int minutes)
    {
        using var owner = factory.AsOwner();
        using var response = await owner.PutAsJsonAsync("/api/v1/settings/compliance-rules/C-04", new { value = new { drivingMinutes = minutes, breakMinutes = 45, splitFirstMinutes = 15, splitLastMinutes = 30 } }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private Shift NewShift()
    {
        var start = new DateTimeOffset(2026, 11, 2, 7, 0, 0, TimeSpan.Zero);
        var shift = ShiftRules.Start(Guid.NewGuid(), employeeId, Day, start, null);
        ShiftRules.End(shift, start.AddHours(1));
        shift.Activities.Single().Type = ActivityType.Driving;
        shift.Status = ShiftStatus.Submitted;
        shift.Version = 1;
        return shift;
    }

    private async Task<Shift> Seed(ShiftStatus status = ShiftStatus.Submitted, ActivityType activityType = ActivityType.Driving)
    {
        var shift = NewShift();
        shift.Status = status;
        shift.Activities.Single().Type = activityType;
        if (status == ShiftStatus.Running)
        {
            shift.EndAt = null;
            shift.Activities.Single().EndAt = null;
        }
        await using var database = Database();
        database.Shifts.Add(shift);
        await database.SaveChangesAsync(Cancellation);
        return shift;
    }

    private async Task Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal(code, body.GetProperty("code").GetString());
    }

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "rule_correction_setup" };
    }
}




