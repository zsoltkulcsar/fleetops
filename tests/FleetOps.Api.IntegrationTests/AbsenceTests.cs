using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api;
using FleetOps.Migrations;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using FleetOps.Modules.Setup;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FleetOps.Tests;

public sealed class AbsenceTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private Guid employeeId;
    private static readonly DateOnly Day = new(2026, 11, 6);
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        await ReadinessTestSupport.AddEmployee(factory, VehicleApiFactory.ActorId, "[\"hr\",\"dispatcher\"]");
        employeeId = await AlertTestSupport.AddEmployee(factory);
        await using var database = Database();
        var employee = await database.Employees.SingleAsync(row => row.Id == employeeId, Cancellation);
        employee.VacationEntitlementDays = 20;
        employee.VacationOpeningBalance = 3;
        employee.VacationOpeningBalanceYear = 2026;
        await database.SaveChangesAsync(Cancellation);
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Driver_vacation_request_is_idempotent_by_command_id()
    {
        var absenceId = Guid.NewGuid();
        var command = Command("absence.request", absenceId, new { from = Day, to = Day });
        var first = await Batch(command);
        var second = await Batch(command);
        Assert.Equal(first, second);
        await using var database = Database();
        var absence = Assert.Single(await database.Absences.ToListAsync(Cancellation));
        Assert.Equal(absenceId, absence.Id);
        Assert.Equal(command.CommandId, absence.CommandId);
        Assert.Equal(AbsenceStatus.Requested, absence.Status);
        Assert.Single(await database.CommandReceipts.Where(row => row.CommandId == command.CommandId).ToListAsync(Cancellation));
    }

    [Fact]
    public async Task Half_day_is_only_allowed_for_a_single_day()
    {
        using var hr = factory.AsHr();
        await Refused(hr, "/api/v1/absences", Record(Day, Day.AddDays(1), halfDay: HalfDay.Am), HttpStatusCode.BadRequest, "validation_error", "halfDay");
        var accepted = await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day, Day, halfDay: HalfDay.Am));
        await using var database = Database();
        Assert.Equal(HalfDay.Am, Assert.Single(await database.Absences.ToListAsync(Cancellation)).HalfDay);
        Assert.Equal(Day, accepted.To);
    }

    [Fact]
    public async Task Decline_needs_a_reason_and_tells_the_driver()
    {
        var id = await Request();
        using (var hr = factory.AsHr())
        {
            await Refused(hr, $"/api/v1/absences/{id}/decide", new { approve = false }, HttpStatusCode.BadRequest, "validation_error", "reason");
            await Post<AbsenceView>(hr, $"/api/v1/absences/{id}/decide", new { approve = false, reason = "Coverage needed" });
        }
        using var driver = factory.AsDriver(employeeId);
        var snapshot = await Get<JsonElement>(driver, "/api/v1/driver/snapshot");
        Assert.Equal("Coverage needed", snapshot.GetProperty("absences")[0].GetProperty("declineReason").GetString());
        await using var database = Database();
        Assert.Equal(AbsenceStatus.Declined, (await database.Absences.SingleAsync(Cancellation)).Status);
        Assert.True(await database.Notifications.AnyAsync(row => row.EmployeeId == employeeId && row.TemplateCode == "vacation_decided", Cancellation));
    }

    [Fact]
    public async Task Balance_is_opening_plus_entitlement_minus_approved_days_with_half_days()
    {
        using (var hr = factory.AsHr())
        {
            await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day, Day));
            await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day.AddDays(3), Day.AddDays(3), halfDay: HalfDay.Pm));
            await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day.AddDays(4), Day.AddDays(4), AbsenceType.Other));
        }
        await Request(Day.AddDays(5));
        var balance = await Balance();
        Assert.Equal(3, balance.Opening);
        Assert.Equal(20, balance.Entitlement);
        Assert.Equal(1.5m, balance.Used);
        Assert.Equal(21.5m, balance.Remaining);
        await using var database = Database();
        Assert.Equal(4, await database.Absences.CountAsync(Cancellation));
    }

    [Fact]
    public async Task Counted_days_skip_weekends_and_company_holidays()
    {
        await using (var setup = Database())
        {
            setup.CompanyHolidays.Add(new CompanyHoliday { Date = Day.AddDays(3), Name = "Test holiday" });
            await setup.SaveChangesAsync(Cancellation);
        }
        using var hr = factory.AsHr();
        var preview = await Get<AbsencePreview>(hr, $"/api/v1/absences/preview?employeeId={employeeId}&from=2026-11-06&to=2026-11-10&type=vacation");
        Assert.Equal(2, preview.CountedDays);
        await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day, Day.AddDays(4)));
        Assert.Equal(2, (await Balance()).Used);
        await using var database = Database();
        Assert.Equal(Day.AddDays(4), (await database.Absences.SingleAsync(Cancellation)).To);
    }

    [Fact]
    public async Task Cancelling_approved_vacation_returns_the_days()
    {
        using var hr = factory.AsHr();
        var absence = await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day, Day));
        Assert.Equal(22, (await Balance()).Remaining);
        await Post<AbsenceView>(hr, $"/api/v1/absences/{absence.Id}/cancel", new { reason = "Plans changed" });
        Assert.Equal(23, (await Balance()).Remaining);
        await using var database = Database();
        var stored = await database.Absences.SingleAsync(Cancellation);
        Assert.Equal(AbsenceStatus.Cancelled, stored.Status);
        Assert.Equal("Plans changed", stored.CancelReason);
    }

    [Fact]
    public async Task Over_balance_request_can_still_be_approved_and_shows_the_warning_flag()
    {
        await using (var setup = Database())
        {
            var employee = await setup.Employees.SingleAsync(row => row.Id == employeeId, Cancellation);
            employee.VacationEntitlementDays = 0;
            employee.VacationOpeningBalance = 0;
            await setup.SaveChangesAsync(Cancellation);
        }
        var id = await Request();
        using var hr = factory.AsHr();
        var preview = await Get<AbsencePreview>(hr, $"/api/v1/absences/preview?employeeId={employeeId}&from=2026-11-06&to=2026-11-06&type=vacation");
        Assert.True(preview.OverBalance);
        Assert.Equal(-1, preview.BalanceAfter);
        await Post<AbsenceView>(hr, $"/api/v1/absences/{id}/decide", new { approve = true });
        await using var database = Database();
        Assert.Equal(AbsenceStatus.Approved, (await database.Absences.SingleAsync(Cancellation)).Status);
        Assert.Equal(-1, (await Balance()).Remaining);
    }

    [Fact]
    public async Task Dispatcher_records_sick_or_other_only_and_hr_is_told()
    {
        var hrId = await AlertTestSupport.AddEmployee(factory, "[\"hr\"]");
        using var dispatcher = factory.AsDispatcher();
        foreach (var type in new[] { AbsenceType.Vacation, AbsenceType.Training, AbsenceType.Unpaid, AbsenceType.TimeOffInLieu })
        {
            await Refused(dispatcher, "/api/v1/absences", Record(Day, Day, type), HttpStatusCode.Forbidden, "permission_denied");
        }
        foreach (var type in new[] { AbsenceType.Sick, AbsenceType.Other })
        {
            await Post<AbsenceView>(dispatcher, "/api/v1/absences", Record(Day, Day, type));
        }
        await using var database = Database();
        Assert.Equal(2, await database.Absences.CountAsync(Cancellation));
        Assert.Equal(2, await database.Notifications.CountAsync(row => row.EmployeeId == hrId && row.TemplateCode == "absence_recorded", Cancellation));
    }

    [Fact]
    public async Task Sick_absence_refuses_a_note()
    {
        using var hr = factory.AsHr();
        await Refused(hr, "/api/v1/absences", Record(Day, Day, AbsenceType.Sick, note: "Private note"), HttpStatusCode.BadRequest, "validation_error", "note");
        await using var database = Database();
        Assert.Empty(await database.Absences.ToListAsync(Cancellation));
    }

    [Fact]
    public async Task Absence_over_shifts_needs_confirmation_and_deletes_none()
    {
        var shiftId = await SeedShift(false);
        using var hr = factory.AsHr();
        var problem = await Refused(hr, "/api/v1/absences", Record(Day, Day), HttpStatusCode.Conflict, "absence_overlaps_shifts");
        Assert.Equal(shiftId, problem.GetProperty("shifts")[0].GetProperty("id").GetGuid());
        await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day, Day, confirm: true));
        await using var database = Database();
        var shift = Assert.Single(await database.Shifts.ToListAsync(Cancellation));
        Assert.Equal(shiftId, shift.Id);
        Assert.Equal(ShiftStatus.Draft, shift.Status);
        Assert.Single(await database.Absences.ToListAsync(Cancellation));
    }

    [Fact]
    public async Task Absence_with_a_running_shift_is_refused_until_it_ends()
    {
        var shiftId = await SeedShift(true);
        using (var hr = factory.AsHr())
        {
            await Refused(hr, "/api/v1/absences", Record(Day, Day, confirm: true), HttpStatusCode.Conflict, "running_shift");
        }
        await Batch(Command("shift.end", shiftId, new { }, 1));
        using (var hr = factory.AsHr())
        {
            await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day, Day, confirm: true));
        }
        await using var database = Database();
        Assert.NotNull((await database.Shifts.SingleAsync(Cancellation)).EndAt);
        Assert.Single(await database.Absences.ToListAsync(Cancellation));
    }

    [Fact]
    public async Task Approved_absence_over_a_cycle_opens_a21()
    {
        var cycle = await SeedCycle();
        await AlertTestSupport.Apply(factory, "cycle", cycle.Id);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-21", cycle.Id));
        using var hr = factory.AsHr();
        await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day, Day));
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-21", cycle.Id));
        await using var database = Database();
        Assert.Equal(AbsenceStatus.Approved, (await database.Absences.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task A09_does_not_fire_on_an_approved_absence_day()
    {
        await SeedCycle(Day.AddDays(-1), Day);
        await AlertTestSupport.Apply(factory, "employee", employeeId);
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-09", employeeId, scope: "2026-11-05"));
        using var hr = factory.AsHr();
        await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day.AddDays(-1), Day.AddDays(-1)));
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-09", employeeId, scope: "2026-11-05"));
        await using var database = Database();
        Assert.Equal(AbsenceStatus.Approved, (await database.Absences.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task Start_on_an_absence_day_needs_confirmation_and_opens_a18()
    {
        using (var hr = factory.AsHr())
        {
            await Post<AbsenceView>(hr, "/api/v1/absences", Record(Day, Day));
        }
        var shiftId = Guid.NewGuid();
        var refused = await Batch(Command("shift.start", shiftId, new { workWithoutVehicle = true }), "confirmation_required", "absence_today");
        Assert.Equal("vacation", refused.AbsenceType);
        await using (var check = Database())
        {
            Assert.Empty(await check.Shifts.ToListAsync(Cancellation));
        }
        await Batch(Command("shift.start", shiftId, new { workWithoutVehicle = true, confirmAbsence = true }));
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-18", shiftId));
        await using var database = Database();
        Assert.Equal(shiftId, (await database.Shifts.SingleAsync(Cancellation)).Id);
    }

    [Fact]
    public async Task Schedule_change_request_opens_a19_and_a_new_one_supersedes_it()
    {
        var cycle = await SeedCycle();
        var first = await ScheduleRequest(cycle.Id);
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-19", first));
        var second = await ScheduleRequest(cycle.Id);
        await using var database = Database();
        Assert.Equal(ScheduleRequestStatus.Superseded, (await database.ScheduleChangeRequests.SingleAsync(row => row.Id == first, Cancellation)).Status);
        Assert.Equal(ScheduleRequestStatus.Open, (await database.ScheduleChangeRequests.SingleAsync(row => row.Id == second, Cancellation)).Status);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-19", first));
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-19", second));
    }

    [Fact]
    public async Task Accepting_a_schedule_change_changes_that_cycle_not_the_pattern()
    {
        var cycle = await SeedCycle();
        var id = await ScheduleRequest(cycle.Id);
        using var dispatcher = factory.AsDispatcher();
        await Post<ScheduleRequestView>(dispatcher, $"/api/v1/schedule/requests/{id}/decide", new { accept = true });
        await using var database = Database();
        var stored = await database.Cycles.SingleAsync(row => row.Id == cycle.Id, Cancellation);
        Assert.Equal(Day.AddDays(3), stored.From);
        Assert.Equal(Day.AddDays(4), stored.To);
        var employee = await database.Employees.SingleAsync(row => row.Id == employeeId, Cancellation);
        Assert.Equal(new CyclePattern(2, 2, Day, cycle.VehicleId), employee.CyclePattern);
        Assert.Equal(ScheduleRequestStatus.Accepted, (await database.ScheduleChangeRequests.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task Declining_a_schedule_change_shows_the_reason_to_the_driver()
    {
        var cycle = await SeedCycle();
        var id = await ScheduleRequest(cycle.Id);
        using (var dispatcher = factory.AsDispatcher())
        {
            await Post<ScheduleRequestView>(dispatcher, $"/api/v1/schedule/requests/{id}/decide", new { accept = false, reason = "Truck needed" });
        }
        using var driver = factory.AsDriver(employeeId);
        var snapshot = await Get<JsonElement>(driver, "/api/v1/driver/snapshot");
        Assert.Equal("Truck needed", snapshot.GetProperty("scheduleRequests")[0].GetProperty("declineReason").GetString());
        await using var database = Database();
        Assert.Equal(ScheduleRequestStatus.Declined, (await database.ScheduleChangeRequests.SingleAsync(Cancellation)).Status);
        Assert.Equal(Day, (await database.Cycles.SingleAsync(Cancellation)).From);
    }

    [Fact]
    public async Task Drivers_cannot_decide_or_record_absences()
    {
        var id = await Request();
        using var driver = factory.AsDriver(employeeId);
        await Refused(driver, "/api/v1/absences", Record(Day, Day, AbsenceType.Sick), HttpStatusCode.Forbidden, "permission_denied");
        await Refused(driver, $"/api/v1/absences/{id}/decide", new { approve = true }, HttpStatusCode.Forbidden, "permission_denied");
        await using var database = Database();
        Assert.Equal(AbsenceStatus.Requested, Assert.Single(await database.Absences.ToListAsync(Cancellation)).Status);
    }

    private RecordAbsenceCommand Record(DateOnly from, DateOnly to, AbsenceType type = AbsenceType.Vacation, HalfDay? halfDay = null, string? note = null, bool confirm = false) => new(employeeId, from, to, halfDay, type, note, confirm);

    private DriverCommand Command(string type, Guid id, object payload, int version = 0) => new(Guid.NewGuid(), type, 1, id, version, factory.Clock.GetUtcNow(), JsonSerializer.SerializeToElement(payload, VehicleApiFactory.JsonOptions));

    private async Task<DriverCommandResult> Batch(DriverCommand command, string status = "applied", string? code = null)
    {
        using var driver = factory.AsDriver(employeeId);
        var result = await Post<DriverCommandBatchResult>(driver, "/api/v1/driver/commands/batch", new DriverCommandBatch([command]));
        var outcome = Assert.Single(result.Results);
        Assert.Equal(status, outcome.Status);
        Assert.Equal(code, outcome.Code);
        return outcome;
    }

    private async Task<Guid> Request(DateOnly? date = null)
    {
        var id = Guid.NewGuid();
        await Batch(Command("absence.request", id, new { from = date ?? Day, to = date ?? Day }));
        return id;
    }

    private async Task<Guid> ScheduleRequest(Guid cycleId)
    {
        var id = Guid.NewGuid();
        await Batch(Command("schedule.request_change", id, new { cycleId, proposedFrom = Day.AddDays(3), proposedTo = Day.AddDays(4), note = "Family commitment" }));
        return id;
    }

    private async Task<VacationBalance> Balance()
    {
        using var hr = factory.AsHr();
        return await Get<VacationBalance>(hr, $"/api/v1/people/{employeeId}/vacation-balance?year=2026");
    }

    private async Task<Guid> SeedShift(bool running)
    {
        var id = Guid.NewGuid();
        await using var database = Database();
        var shift = ShiftRules.Start(id, employeeId, Day, factory.Clock.GetUtcNow().AddHours(-2), null);
        if (!running)
        {
            ShiftRules.End(shift, factory.Clock.GetUtcNow().AddHours(-1));
        }
        database.Shifts.Add(shift);
        await database.SaveChangesAsync(Cancellation);
        return id;
    }

    private async Task<Cycle> SeedCycle(DateOnly? from = null, DateOnly? to = null)
    {
        Guid truckId;
        using (var dispatcher = factory.AsDispatcher())
        {
            truckId = (await factory.CreateTruck(dispatcher)).Id;
        }
        await ReadinessTestSupport.AddAllVehicleDocuments(factory, truckId, Day.AddYears(1));
        await using var database = Database();
        var cycle = new Cycle { EmployeeId = employeeId, VehicleId = truckId, From = from ?? Day, To = to ?? Day.AddDays(7), PatternSlot = 0, Status = CycleStatus.Active };
        database.Cycles.Add(cycle);
        var employee = await database.Employees.SingleAsync(row => row.Id == employeeId, Cancellation);
        employee.CyclePattern = new(2, 2, Day, truckId);
        await database.SaveChangesAsync(Cancellation);
        return cycle;
    }

    private async Task<T> Get<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(VehicleApiFactory.JsonOptions, Cancellation))!;
    }

    private async Task<T> Post<T>(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request, VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.False(json.TryGetProperty("code", out _));
        return json.Deserialize<T>(VehicleApiFactory.JsonOptions)!;
    }

    private async Task<JsonElement> Refused(HttpClient client, string path, object request, HttpStatusCode status, string code, string? field = null)
    {
        using var response = await client.PostAsJsonAsync(path, request, VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        if (field is not null)
        {
            Assert.Contains(problem.GetProperty("fields").EnumerateArray(), value => value.GetString() == field);
        }
        return problem;
    }

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "absence_test_setup" };
    }
}
