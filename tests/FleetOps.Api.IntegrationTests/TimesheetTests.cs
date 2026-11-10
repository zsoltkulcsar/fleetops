using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api;
using FleetOps.Migrations;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public sealed class TimesheetTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private Guid employeeId;
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private static readonly DateOnly Monday = new(2026, 11, 2);

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        employeeId = await Employee();
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Week_grid_lists_drivers_by_day_with_shifts_absences_missing_days_and_warnings()
    {
        await Cycle();
        var shift = await Seed(flagged: true);
        await using (var setup = Database())
        {
            setup.Absences.Add(new Absence { EmployeeId = employeeId, From = Monday.AddDays(1), To = Monday.AddDays(1), Type = AbsenceType.Vacation, Status = AbsenceStatus.Approved });
            await setup.SaveChangesAsync(Cancellation);
        }
        using var office = factory.AsDispatcher();
        var row = Assert.Single((await Grid(office)).Rows);
        Assert.Equal(employeeId, row.EmployeeId);
        Assert.Equal(Enumerable.Range(0, 7).Select(Monday.AddDays), row.Days.Select(day => day.Date));
        Assert.Equal(shift.Id, Assert.Single(row.Days[0].Shifts).Id);
        Assert.Equal(480, row.Days[0].WorkMinutes);
        Assert.Equal("Awaiting approval", row.Days[0].Label);
        Assert.True(row.Days[0].Warnings);
        Assert.Contains("Vacation", row.Days[1].Absences);
        Assert.False(row.Days[1].Missing);
        Assert.True(row.Days[2].Missing);
        Assert.Equal("Missing", row.Days[2].Label);
        Assert.False(row.Days[6].Missing);
    }

    [Fact]
    public async Task Week_grid_filters_and_pages_on_the_server()
    {
        var second = await Employee("Zulu", "PN-002");
        var third = await Employee("Zulu", "PN-003");
        await Seed();
        await Seed(employee: second, flagged: true);
        await Seed(employee: third, status: ShiftStatus.Draft);
        await Cycle();
        using var office = factory.AsDispatcher();
        var firstPage = await Grid(office, "&pageSize=1");
        var secondPage = await Grid(office, "&pageSize=1&page=2");
        Assert.Equal(3, firstPage.Total);
        Assert.Equal(1, firstPage.PageSize);
        Assert.Equal(2, secondPage.Page);
        Assert.NotEqual(Assert.Single(firstPage.Rows).EmployeeId, Assert.Single(secondPage.Rows).EmployeeId);
        var awaiting = await Grid(office, "&filter=awaiting");
        Assert.Equal(2, awaiting.Total);
        Assert.DoesNotContain(awaiting.Rows, row => row.EmployeeId == third);
        Assert.Equal(second, Assert.Single((await Grid(office, "&filter=warnings")).Rows).EmployeeId);
        Assert.Equal(employeeId, Assert.Single((await Grid(office, "&filter=missing")).Rows).EmployeeId);
        Assert.Equal(third, Assert.Single((await Grid(office, "&search=PN-003")).Rows).EmployeeId);
        Assert.Equal(2, firstPage.Counts.Awaiting);
        Assert.Equal(1, firstPage.Counts.Warnings);
        Assert.Empty((await Grid(office, "&page=9&pageSize=1")).Rows);
    }

    [Fact]
    public async Task Week_grid_is_forbidden_to_drivers()
    {
        await Seed();
        using var driver = factory.AsDriver(employeeId);
        using var response = await driver.GetAsync("/api/v1/timesheets/week?week=2026-11-02", Cancellation);
        await Error(response, HttpStatusCode.Forbidden, "permission_denied");
        await using var db = Database();
        Assert.Equal(ShiftStatus.Submitted, (await db.Shifts.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task Bulk_approve_returns_a_result_per_shift_and_commits_each_independently()
    {
        var first = await Seed();
        var refused = await Seed(day: 1, flagged: true);
        var last = await Seed(day: 2);
        using var office = factory.AsDispatcher();
        var result = await Approve(office, new(first.Id, 1, null), new(refused.Id, 1, null), new(last.Id, 1, null));
        Assert.Equal(2, result.Approved);
        Assert.Equal(1, result.Failed);
        Assert.Equal(new[] { first.Id, refused.Id, last.Id }, result.Results.Select(item => item.ShiftId));
        Assert.Equal(new[] { "approved", "failed", "approved" }, result.Results.Select(item => item.Status));
        Assert.Equal("note_required", result.Results[1].Code);
        Assert.Null(result.Results[0].Code);
        Assert.Null(result.Results[2].Code);
        await using var db = Database();
        Assert.Equal(2, await db.Shifts.CountAsync(row => row.Status == ShiftStatus.Approved, Cancellation));
        Assert.Equal(ShiftStatus.Submitted, (await db.Shifts.SingleAsync(row => row.Id == refused.Id, Cancellation)).Status);
        Assert.Equal(2, await db.AuditEntries.CountAsync(row => row.FieldName == "Status" && row.NewValue == "Approved", Cancellation));
    }

    [Fact]
    public async Task Approving_a_flagged_shift_without_a_note_fails_with_note_required()
    {
        var shift = await Seed(flagged: true);
        using var office = factory.AsDispatcher();
        var refusal = Assert.Single((await Approve(office, new BulkApprovalItem(shift.Id, 1, null))).Results);
        Assert.Equal("failed", refusal.Status);
        Assert.Equal("note_required", refusal.Code);
        await using (var db = Database())
        {
            Assert.Equal(1, (await db.Shifts.SingleAsync(Cancellation)).Version);
            Assert.Equal(ShiftStatus.Submitted, (await db.Shifts.SingleAsync(Cancellation)).Status);
        }
        var accepted = Assert.Single((await Approve(office, new BulkApprovalItem(shift.Id, 1, "Clock checked"))).Results);
        Assert.Equal("approved", accepted.Status);
        Assert.Null(accepted.Code);
        await using var verify = Database();
        Assert.Equal("Clock checked", (await verify.Shifts.SingleAsync(Cancellation)).ApprovalNote);
    }

    [Fact]
    public async Task Approvers_cannot_approve_their_own_shift()
    {
        var shift = await Seed();
        using var own = factory.AsHr(employeeId);
        var result = Assert.Single((await Approve(own, new BulkApprovalItem(shift.Id, 1, null))).Results);
        Assert.Equal("failed", result.Status);
        Assert.Equal("own_shift", result.Code);
        await using var db = Database();
        Assert.Equal(ShiftStatus.Submitted, (await db.Shifts.SingleAsync(Cancellation)).Status);
        Assert.Null((await db.Shifts.SingleAsync(Cancellation)).ApprovedBy);
    }

    [Fact]
    public async Task Only_hr_can_approve_an_owners_shift()
    {
        await using (var setup = Database())
        {
            (await setup.Employees.SingleAsync(Cancellation)).Roles = "[\"driver\",\"owner\"]";
            await setup.SaveChangesAsync(Cancellation);
        }
        var shift = await Seed();
        using (var dispatcher = factory.AsDispatcher())
        {
            var result = Assert.Single((await Approve(dispatcher, new BulkApprovalItem(shift.Id, 1, null))).Results);
            Assert.Equal("failed", result.Status);
            Assert.Equal("owner_shift_needs_hr", result.Code);
        }
        using (var owner = factory.AsOwner())
            Assert.Equal("owner_shift_needs_hr", Assert.Single((await Approve(owner, new BulkApprovalItem(shift.Id, 1, null))).Results).Code);
        await using (var unchanged = Database())
            Assert.Equal(ShiftStatus.Submitted, (await unchanged.Shifts.SingleAsync(Cancellation)).Status);
        using var hr = factory.AsHr();
        Assert.Equal("approved", Assert.Single((await Approve(hr, new BulkApprovalItem(shift.Id, 1, null))).Results).Status);
        await using var db = Database();
        Assert.Equal(VehicleApiFactory.ActorId, (await db.Shifts.SingleAsync(Cancellation)).ApprovedBy);
        Assert.Equal(ShiftStatus.Approved, (await db.Shifts.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task Any_other_approver_can_approve_a_waiting_shift()
    {
        foreach (var role in new[] { "dispatcher", "hr", "owner" })
        {
            var shift = await Seed(day: role == "dispatcher" ? 0 : role == "hr" ? 1 : 2);
            using var office = role == "dispatcher" ? factory.AsDispatcher() : role == "hr" ? factory.AsHr() : factory.AsOwner();
            var result = Assert.Single((await Approve(office, new BulkApprovalItem(shift.Id, 1, null))).Results);
            Assert.Equal("approved", result.Status);
            Assert.Null(result.Code);
            await using var db = Database();
            var stored = await db.Shifts.SingleAsync(row => row.Id == shift.Id, Cancellation);
            Assert.Equal(ShiftStatus.Approved, stored.Status);
            Assert.Equal(VehicleApiFactory.ActorId, stored.ApprovedBy);
            Assert.Equal(factory.Clock.GetUtcNow(), stored.ApprovedAt);
        }
    }

    [Fact]
    public async Task A10_opens_for_shifts_submitted_over_three_days_and_near_the_cutoff_and_clears_on_approve()
    {
        var old = await Seed(submittedAt: factory.Clock.GetUtcNow().AddDays(-4));
        var recent = await Seed(day: 1);
        await Setting("company.payroll_cutoff_day", "20");
        await Sweep();
        await using (var db = Database())
        {
            Assert.True(await db.Alerts.AnyAsync(row => row.RuleId == "A-10" && row.SubjectId == old.Id && row.Status == AlertStatus.Open, Cancellation));
            Assert.False(await db.Alerts.AnyAsync(row => row.RuleId == "A-10" && row.SubjectId == recent.Id, Cancellation));
        }
        await Setting("company.payroll_cutoff_day", "8");
        await Sweep();
        await using (var db = Database())
            Assert.True(await db.Alerts.AnyAsync(row => row.RuleId == "A-10" && row.SubjectId == recent.Id && row.Status == AlertStatus.Open, Cancellation));
        using var office = factory.AsDispatcher();
        Assert.Equal(2, (await Approve(office, new(old.Id, 1, null), new(recent.Id, 1, null))).Approved);
        await using var verify = Database();
        Assert.Equal(2, await verify.Alerts.CountAsync(row => row.RuleId == "A-10" && row.Status == AlertStatus.Resolved, Cancellation));
    }

    [Fact]
    public async Task Shift_detail_returns_activities_overnights_flags_and_history()
    {
        var shift = await Seed(flagged: true);
        await using (var setup = Database())
        {
            setup.Overnights.Add(new Overnight { ShiftId = shift.Id, Night = Monday, CountryCode = "DE", Kind = OvernightKind.Cab });
            await setup.SaveChangesAsync(Cancellation);
        }
        using var office = factory.AsDispatcher();
        var detail = await Get<ShiftDetail>(office, $"/api/v1/timesheets/shifts/{shift.Id}");
        Assert.Equal(employeeId, detail.EmployeeId);
        Assert.Equal(shift.Activities[0].Id, Assert.Single(detail.Shift.Activities).Id);
        Assert.Equal(8m, detail.Shift.OtherWorkHours);
        Assert.Equal("DE", Assert.Single(detail.Shift.Overnights).CountryCode);
        Assert.Equal(OvernightKind.Cab, Assert.Single(detail.Shift.Overnights).Kind);
        Assert.Contains(detail.Flags, flag => flag.Contains("Phone clock"));
        Assert.Contains(detail.History, entry => entry.Field == "Status" && entry.Reason == "timesheet_setup" && entry.EntityId == shift.Id);
    }

    [Fact]
    public async Task Rejecting_requires_a_reason_and_the_driver_snapshot_shows_it()
    {
        var shift = await Seed();
        using (var office = factory.AsDispatcher())
        {
            using var invalid = await office.PostAsJsonAsync($"/api/v1/timesheets/shifts/{shift.Id}/reject", new RejectShiftRequest(1, " "), Cancellation);
            await Error(invalid, HttpStatusCode.BadRequest, "validation_error", "reason");
            await using (var unchanged = Database())
                Assert.Equal(ShiftStatus.Submitted, (await unchanged.Shifts.SingleAsync(Cancellation)).Status);
            using var response = await office.PostAsJsonAsync($"/api/v1/timesheets/shifts/{shift.Id}/reject", new RejectShiftRequest(1, "Correct break"), Cancellation);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var driver = factory.AsDriver(employeeId);
        var snapshot = Assert.Single((await Get<ShiftSnapshot>(driver, "/api/v1/driver/shifts")).Shifts);
        Assert.Equal("Correct break", snapshot.RejectReason);
        Assert.Equal(ShiftStatus.Rejected, snapshot.Status);
        await using var db = Database();
        Assert.Equal("Correct break", (await db.Shifts.SingleAsync(Cancellation)).RejectReason);
        Assert.True(await db.Notifications.AnyAsync(row => row.EmployeeId == employeeId && row.TemplateCode == "shift_rejected", Cancellation));
    }

    [Fact]
    public async Task Office_edit_requires_a_reason_is_audited_and_notifies_the_driver_with_the_difference()
    {
        var shift = await Seed();
        using (var office = factory.AsDispatcher())
        {
            using var invalid = await Edit(office, shift, " ");
            await Error(invalid, HttpStatusCode.BadRequest, "validation_error", "reason");
            await using (var unchanged = Database())
                Assert.Equal(shift.EndAt, (await unchanged.Shifts.SingleAsync(Cancellation)).EndAt);
            using var response = await Edit(office, shift);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        await using var db = Database();
        var stored = await db.Shifts.SingleAsync(Cancellation);
        Assert.Equal(shift.EndAt!.Value.AddHours(1), stored.EndAt);
        Assert.Equal(2, stored.LastOfficeVersion);
        Assert.True(await db.AuditEntries.AnyAsync(row => row.EntityId == shift.Id && row.FieldName == "EndAt" && row.OldValue != row.NewValue && row.Reason == "Correct hours", Cancellation));
        var notification = await db.Notifications.SingleAsync(row => row.EmployeeId == employeeId && row.TemplateCode == "shift_edited_by_office", Cancellation);
        var parameters = JsonSerializer.Deserialize<JsonElement>(notification.Parameters);
        Assert.Equal(shift.Id, parameters.GetProperty("shiftId").GetGuid());
        var auditId = parameters.GetProperty("auditId").GetGuid();
        Assert.True(await db.AuditEntries.AnyAsync(row => row.Id == auditId && row.EntityId == shift.Id && row.FieldName == "OfficeChange", Cancellation));
        using var driver = factory.AsDriver(employeeId);
        var changes = Assert.Single((await Get<ShiftSnapshot>(driver, "/api/v1/driver/shifts")).Shifts).OfficeChanges;
        var difference = Assert.Single(changes, row => row.EntityId == shift.Id && row.Field == "EndAt");
        Assert.Equal(shift.EndAt, DateTimeOffset.Parse(difference.Before!));
        Assert.Equal(stored.EndAt, DateTimeOffset.Parse(difference.After!));
        Assert.Equal("Correct hours", difference.Reason);
    }

    [Fact]
    public async Task Editing_an_approved_shift_returns_it_to_submitted()
    {
        var shift = await Seed(status: ShiftStatus.Approved);
        using var office = factory.AsDispatcher();
        using var response = await Edit(office, shift);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<OfficeShiftResult>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(ShiftStatus.Submitted, result!.Status);
        await using var db = Database();
        var stored = await db.Shifts.SingleAsync(Cancellation);
        Assert.Equal(ShiftStatus.Submitted, stored.Status);
        Assert.Equal(factory.Clock.GetUtcNow(), stored.SubmittedAt);
        Assert.Null(stored.ApprovedBy);
        Assert.Null(stored.ApprovedAt);
        Assert.Null(stored.ApprovalNote);
    }

    [Fact]
    public async Task Office_edit_makes_an_older_driver_command_a_conflict()
    {
        var shift = await Seed(status: ShiftStatus.Draft);
        using (var office = factory.AsDispatcher())
        {
            using var response = await Edit(office, shift);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        using var driver = factory.AsDriver(employeeId);
        var command = Command("shift.submit", shift.Id, 1);
        var result = await Batch(driver, command);
        Assert.Equal("conflict", result.Status);
        Assert.Equal("office_version_stands", result.Code);
        await using var db = Database();
        var conflict = await db.ShiftConflicts.SingleAsync(Cancellation);
        Assert.Equal(command.CommandId, conflict.CommandId);
        Assert.Equal(2, conflict.OfficeVersion);
        Assert.Equal(1, conflict.BaseVersion);
        Assert.Equal(shift.EndAt!.Value.AddHours(1), (await db.Shifts.SingleAsync(Cancellation)).EndAt);
        Assert.True(await db.Alerts.AnyAsync(row => row.RuleId == "A-17" && row.SubjectId == employeeId, Cancellation));
    }

    [Fact]
    public async Task Editing_an_exported_shift_is_refused()
    {
        var shift = await Seed(status: ShiftStatus.Exported);
        using var office = factory.AsDispatcher();
        using var response = await Edit(office, shift);
        await Error(response, HttpStatusCode.Conflict, "locked");
        await using var db = Database();
        var stored = await db.Shifts.SingleAsync(Cancellation);
        Assert.Equal(ShiftStatus.Exported, stored.Status);
        Assert.Equal(shift.EndAt, stored.EndAt);
        Assert.Equal(1, stored.Version);
        Assert.False(await db.Notifications.AnyAsync(row => row.TemplateCode == "shift_edited_by_office", Cancellation));
    }

    [Fact]
    public async Task Office_added_shift_has_source_office_and_clears_the_missing_day()
    {
        await Cycle();
        using var office = factory.AsDispatcher();
        Assert.True(Assert.Single((await Grid(office)).Rows).Days[0].Missing);
        await Sweep();
        await using (var missing = Database())
            Assert.True(await missing.Alerts.AnyAsync(row => row.RuleId == "A-09" && row.SubjectId == employeeId && row.ScopeKey == "2026-11-02" && row.Status == AlertStatus.Open, Cancellation));
        var result = await Add(office);
        var cell = Assert.Single((await Grid(office)).Rows).Days[0];
        Assert.False(cell.Missing);
        Assert.Equal(result.ShiftId, Assert.Single(cell.Shifts).Id);
        await Sweep();
        await using var db = Database();
        var stored = await db.Shifts.SingleAsync(Cancellation);
        Assert.Equal(ShiftSource.Office, stored.Source);
        Assert.Equal(ShiftStatus.Submitted, stored.Status);
        Assert.Equal(factory.Clock.GetUtcNow(), stored.SubmittedAt);
        Assert.False(await db.Alerts.AnyAsync(row => row.RuleId == "A-09" && row.SubjectId == employeeId && row.ScopeKey.Contains("2026-11-02") && row.Status != AlertStatus.Resolved, Cancellation));
    }

    [Fact]
    public async Task Office_added_shift_for_a_blocked_driver_saves_flagged_and_raises_A02()
    {
        using var office = factory.AsDispatcher();
        var result = await Add(office);
        await using var db = Database();
        var shift = await db.Shifts.SingleAsync(Cancellation);
        Assert.Equal(result.ShiftId, shift.Id);
        Assert.Equal("driver_blocked", shift.BlockedAtStart);
        Assert.Equal(ShiftStatus.Submitted, shift.Status);
        Assert.True(await db.Alerts.AnyAsync(row => row.RuleId == "A-02" && row.SubjectId == employeeId && row.ScopeKey.StartsWith("blocked_shift:") && row.Status == AlertStatus.Open, Cancellation));
        Assert.True(Assert.Single((await Grid(office)).Rows).Days[0].Warnings);
    }

    [Fact]
    public async Task Office_ending_a_running_shift_is_audited_and_sets_draft()
    {
        var shift = await Seed(status: ShiftStatus.Running);
        using var office = factory.AsDispatcher();
        var end = shift.StartAt.AddHours(8);
        using var response = await office.PostAsJsonAsync($"/api/v1/timesheets/shifts/{shift.Id}/end", new EndShiftRequest(1, "Driver phone unavailable", end, null), Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Database();
        var stored = await db.Shifts.Include(row => row.Activities).SingleAsync(Cancellation);
        Assert.Equal(ShiftStatus.Draft, stored.Status);
        Assert.Equal(end, stored.EndAt);
        Assert.Equal(end, Assert.Single(stored.Activities).EndAt);
        Assert.Equal(3, stored.LastOfficeVersion);
        Assert.True(await db.AuditEntries.AnyAsync(row => row.EntityId == shift.Id && row.FieldName == "EndAt" && row.Reason == "Driver phone unavailable", Cancellation));
        Assert.True(await db.Notifications.AnyAsync(row => row.EmployeeId == employeeId && row.TemplateCode == "shift_edited_by_office", Cancellation));
    }

    [Fact]
    public async Task A16_opens_past_max_shift_hours_and_never_when_the_limit_is_zero()
    {
        var shift = await Seed(status: ShiftStatus.Running);
        await Setting("company.max_shift_hours", "0");
        await Sweep();
        await using (var db = Database())
            Assert.False(await db.Alerts.AnyAsync(row => row.RuleId == "A-16", Cancellation));
        await Setting("company.max_shift_hours", "200");
        await Sweep();
        await using (var db = Database())
            Assert.False(await db.Alerts.AnyAsync(row => row.RuleId == "A-16", Cancellation));
        await Setting("company.max_shift_hours", "8");
        await Sweep();
        await using (var db = Database())
            Assert.True(await db.Alerts.AnyAsync(row => row.RuleId == "A-16" && row.SubjectId == shift.Id && row.Status == AlertStatus.Open, Cancellation));
        await Setting("company.max_shift_hours", "0");
        await Sweep();
        await using var verify = Database();
        Assert.False(await verify.Alerts.AnyAsync(row => row.RuleId == "A-16" && row.Status != AlertStatus.Resolved, Cancellation));
    }

    [Fact]
    public async Task Driver_submit_sets_submitted_at()
    {
        var shift = await Seed(status: ShiftStatus.Draft);
        using var driver = factory.AsDriver(employeeId);
        var result = await Batch(driver, Command("shift.submit", shift.Id, 1));
        Assert.Equal("applied", result.Status);
        Assert.Null(result.Code);
        await using var db = Database();
        var stored = await db.Shifts.SingleAsync(Cancellation);
        Assert.Equal(ShiftStatus.Submitted, stored.Status);
        Assert.Equal(factory.Clock.GetUtcNow(), stored.SubmittedAt);
        Assert.Equal(stored.SubmittedAt, Assert.Single((await Get<ShiftSnapshot>(driver, "/api/v1/driver/shifts")).Shifts).SubmittedAt);
    }

    private async Task<Guid> Employee(string lastName = "Alpha", string? personnelNumber = null)
    {
        var id = Guid.NewGuid();
        await using var db = Database();
        db.Employees.Add(new Employee { Id = id, FirstName = "Driver", LastName = lastName, PersonnelNumber = personnelNumber, Email = $"{id:N}@example.invalid", Roles = "[\"driver\"]", StartDate = new(2026, 1, 1) });
        await db.SaveChangesAsync(Cancellation);
        return id;
    }

    private async Task<Shift> Seed(int day = 0, Guid? employee = null, ShiftStatus status = ShiftStatus.Submitted, bool flagged = false, DateTimeOffset? submittedAt = null)
    {
        var start = new DateTimeOffset(2026, 11, 2, 7, 0, 0, TimeSpan.Zero).AddDays(day);
        var shift = ShiftRules.Start(Guid.NewGuid(), employee ?? employeeId, Monday.AddDays(day), start, null);
        if (status != ShiftStatus.Running) ShiftRules.End(shift, start.AddHours(8));
        shift.Status = status;
        shift.Version = 1;
        shift.DeviceTimeGap = flagged;
        shift.SubmittedAt = status == ShiftStatus.Submitted ? submittedAt ?? factory.Clock.GetUtcNow() : null;
        if (status == ShiftStatus.Approved)
        {
            shift.ApprovedBy = VehicleApiFactory.ActorId;
            shift.ApprovedAt = factory.Clock.GetUtcNow().AddDays(-1);
            shift.ApprovalNote = "Original approval";
        }
        await using var db = Database();
        db.Shifts.Add(shift);
        await db.SaveChangesAsync(Cancellation);
        return shift;
    }

    private async Task Cycle()
    {
        using var office = factory.AsDispatcher();
        var truck = await factory.CreateTruck(office);
        await using var db = Database();
        db.Cycles.Add(new Cycle { EmployeeId = employeeId, VehicleId = truck.Id, From = Monday, To = Monday.AddDays(6), Status = CycleStatus.Active, Source = CycleSource.Manual });
        await db.SaveChangesAsync(Cancellation);
    }

    private async Task Setting(string key, string value)
    {
        await using var db = Database();
        var setting = await db.Settings.SingleOrDefaultAsync(row => row.Key == key, Cancellation);
        if (setting is null)
        {
            setting = new Setting { Key = key, UpdatedAt = factory.Clock.GetUtcNow(), UpdatedBy = "test" };
            db.Settings.Add(setting);
        }
        setting.Value = int.Parse(value);
        await db.SaveChangesAsync(Cancellation);
    }

    private async Task Sweep()
    {
        factory.Services.GetRequiredService<IConfiguration>()["Jobs:Secret"] = "timesheet-test-only";
        using var client = factory.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "timesheet-test-only");
        using var response = await client.GetAsync("/jobs/alerts-sweep", Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("succeeded", json.GetProperty("status").GetString());
    }

    private Task<WeekGrid> Grid(HttpClient client, string query = "") => Get<WeekGrid>(client, "/api/v1/timesheets/week?week=2026-11-02" + query);

    private async Task<T> Get<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(VehicleApiFactory.JsonOptions, Cancellation))!;
    }

    private async Task<BulkApprovalResult> Approve(HttpClient client, params BulkApprovalItem[] items)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/timesheets/approve", new BulkApprovalRequest(items), VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<BulkApprovalResult>(VehicleApiFactory.JsonOptions, Cancellation))!;
    }

    private Task<HttpResponseMessage> Edit(HttpClient client, Shift shift, string reason = "Correct hours") =>
        client.PutAsJsonAsync($"/api/v1/timesheets/shifts/{shift.Id}", new EditShiftRequest(shift.Version, reason,
            new(shift.Date, shift.StartAt, shift.EndAt!.Value.AddHours(1), null, null, null, null, null, null)), VehicleApiFactory.JsonOptions, Cancellation);

    private async Task<OfficeShiftResult> Add(HttpClient client)
    {
        var start = new DateTimeOffset(2026, 11, 2, 7, 0, 0, TimeSpan.Zero);
        using var response = await client.PostAsJsonAsync("/api/v1/timesheets/shifts", new AddShiftRequest(employeeId, "Missing phone record",
            new(Monday, start, start.AddHours(8), null, null, null, null, null, null)), VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OfficeShiftResult>(VehicleApiFactory.JsonOptions, Cancellation))!;
    }

    private DriverCommand Command(string type, Guid shiftId, int version) => new(Guid.NewGuid(), type, 1, shiftId, version, factory.Clock.GetUtcNow(), JsonSerializer.SerializeToElement(new { }));

    private async Task<DriverCommandResult> Batch(HttpClient client, DriverCommand command)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/driver/commands/batch", new DriverCommandBatch([command]), VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.Single((await response.Content.ReadFromJsonAsync<DriverCommandBatchResult>(VehicleApiFactory.JsonOptions, Cancellation))!.Results);
    }

    private async Task Error(HttpResponseMessage response, HttpStatusCode status, string code, string? field = null)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal(code, body.GetProperty("code").GetString());
        if (field is not null) Assert.Contains(body.GetProperty("fields").EnumerateArray(), item => item.GetString() == field);
    }

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "timesheet_setup" };
    }
}




