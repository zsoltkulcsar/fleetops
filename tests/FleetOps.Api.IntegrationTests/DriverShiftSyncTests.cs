using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api;
using FleetOps.Migrations;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Schedule;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using Xunit;

namespace FleetOps.Tests;

public sealed class DriverShiftSyncTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private Guid employeeId;
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;
    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        employeeId = await Employee();
    }
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Repeated_command_returns_stored_result_and_snapshot_acknowledges_it()
    {
        using var client = factory.AsDriver(employeeId);
        var id = Guid.NewGuid();
        var aggregate = Guid.NewGuid();
        var command = Command("sync.test", aggregate, 0, new { }, id);
        var first = Assert.Single((await Batch(client, command)).Results);
        var replay = Assert.Single((await Batch(client, command)).Results);
        Assert.Equal("applied", first.Status);
        Assert.Null(first.Code);
        Assert.Equal(first, replay);
        await using var db = Database();
        var receipt = Assert.Single(await db.CommandReceipts.ToListAsync(Cancellation));
        Assert.Equal(id, receipt.CommandId);
        Assert.Equal("applied", receipt.Outcome);
        var snapshot = await client.GetFromJsonAsync<ShiftSnapshot>("/api/v1/driver/shifts", VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Contains(id, snapshot!.AppliedCommandIds);
        Assert.Empty(snapshot.Shifts);
    }

    [Fact]
    public async Task Queued_day_starts_switches_ends_records_night_and_submits_in_order()
    {
        using var client = factory.AsDriver(employeeId);
        var shiftId = Guid.NewGuid();
        var start = factory.Clock.GetUtcNow();
        var commands = new[]
        {
            Command("shift.start", shiftId, 0, new { workWithoutVehicle = true }, at: start),
            Command("shift.switch", shiftId, 1, new { type = "availability" }, at: start.AddHours(1)),
            Command("shift.end", shiftId, 2, new { }, at: start.AddHours(8)),
            Command("shift.night", shiftId, 3, new { night = "2026-11-06", countryCode = "DE", kind = "cab" }, at: start.AddHours(8)),
            Command("shift.submit", shiftId, 4, new { }, at: start.AddHours(8)),
        };
        var result = await Batch(client, commands);
        Assert.Equal(5, result.Results.Count);
        Assert.All(result.Results, item => { Assert.Equal("applied", item.Status); Assert.Null(item.Code); });
        await using var db = Database();
        var shift = await db.Shifts.Include(row => row.Activities).Include(row => row.Overnights).SingleAsync(Cancellation);
        Assert.Equal(employeeId, shift.EmployeeId);
        Assert.Equal(ShiftStatus.Submitted, shift.Status);
        Assert.Equal(5, shift.Version);
        Assert.Equal(start.AddHours(8), shift.EndAt);
        var activities = shift.Activities.OrderBy(activity => activity.StartAt).ToArray();
        Assert.Equal(ActivityType.OtherWork, activities[0].Type);
        Assert.Equal(activities[0].EndAt, activities[1].StartAt);
        Assert.Equal(ActivityType.Availability, activities[1].Type);
        Assert.Equal("DE", Assert.Single(shift.Overnights).CountryCode);
        Assert.Equal(5, await db.CommandReceipts.CountAsync(Cancellation));
        Assert.True(await db.AuditEntries.AnyAsync(entry => entry.EntityId == shiftId && entry.FieldName == "Status", Cancellation));
        var snapshot = await client.GetFromJsonAsync<ShiftSnapshot>("/api/v1/driver/shifts", VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(shiftId, Assert.Single(snapshot!.Shifts).Id);
        Assert.Equal(5, snapshot.AppliedCommandIds.Count);
    }

    [Fact]
    public async Task Concurrent_starts_enforce_one_running_shift_and_store_stable_refusal()
    {
        using var client = factory.AsDriver(employeeId);
        var requests = new[] {
            Command("shift.start", Guid.NewGuid(), 0, new { workWithoutVehicle = true }),
            Command("shift.start", Guid.NewGuid(), 0, new { workWithoutVehicle = true })
        };
        var results = await Task.WhenAll(requests.Select(request => Batch(client, request)));
        var outcomes = results.Select(result => Assert.Single(result.Results)).ToArray();
        Assert.Single(outcomes.Where(result => result.Status == "applied"));
        var refused = Assert.Single(outcomes.Where(result => result.Status == "failed"));
        Assert.Equal("running_shift_exists", refused.Code);
        await using var db = Database();
        Assert.Equal(1, await db.Shifts.CountAsync(row => row.Status == ShiftStatus.Running, Cancellation));
        Assert.Equal(2, await db.CommandReceipts.CountAsync(Cancellation));
        var retried = Assert.Single((await Batch(client, requests.Single(request => request.CommandId == refused.CommandId))).Results);
        Assert.Equal("running_shift_exists", retried.Code);
        Assert.Equal("failed", retried.Status);
    }

    [Fact]
    public async Task Locked_shift_keeps_office_data_and_preserves_conflict_and_later_dependency_payloads()
    {
        using var client = factory.AsDriver(employeeId);
        var shiftId = Guid.NewGuid();
        await Batch(client, Command("shift.start", shiftId, 0, new { workWithoutVehicle = true }));
        await using (var setup = Database())
        {
            var shift = await setup.Shifts.Include(row => row.Activities).SingleAsync(Cancellation);
            ShiftRules.End(shift, factory.Clock.GetUtcNow().AddHours(8));
            shift.Status = ShiftStatus.Approved;
            shift.Version++;
            shift.LastOfficeVersion = shift.Version;
            await setup.SaveChangesAsync(Cancellation);
        }
        var conflictId = Guid.NewGuid();
        var dependencyId = Guid.NewGuid();
        var result = await Batch(client,
            Command("shift.switch", shiftId, 1, new { type = "break" }, conflictId),
            Command("shift.end", shiftId, 2, new { odometerKm = 999 }, dependencyId));
        Assert.Equal("conflict", result.Results[0].Status);
        Assert.Equal("office_version_stands", result.Results[0].Code);
        Assert.Equal("failed", result.Results[1].Status);
        Assert.Equal("dependency", result.Results[1].Code);
        await using var db = Database();
        var current = await db.Shifts.Include(row => row.Activities).SingleAsync(Cancellation);
        Assert.Equal(ShiftStatus.Approved, current.Status);
        Assert.Equal(ActivityType.OtherWork, Assert.Single(current.Activities).Type);
        var conflict = Assert.Single(await db.ShiftConflicts.ToListAsync(Cancellation));
        Assert.Equal(conflictId, conflict.CommandId);
        Assert.Contains("break", conflict.DriverPayload);
        Assert.Equal(3, conflict.OfficeVersion);
        Assert.Contains("999", (await db.CommandReceipts.SingleAsync(row => row.CommandId == dependencyId, Cancellation)).Payload);
        Assert.True(await db.Alerts.AnyAsync(alert => alert.RuleId == "A-17" && alert.SubjectId == employeeId, Cancellation));
        Assert.True(await db.Notifications.AnyAsync(notification => notification.EmployeeId == employeeId && notification.TemplateCode == "sync_conflict", Cancellation));
    }

    [Fact]
    public async Task Blocked_offline_start_is_saved_flagged_at_start_date_and_raises_A02()
    {
        Guid truckId;
        using (var dispatcher = factory.AsDispatcher()) truckId = (await factory.CreateTruck(dispatcher)).Id;
        using var client = factory.AsDriver(employeeId);
        var shiftId = Guid.NewGuid();
        var result = Assert.Single((await Batch(client, Command("shift.start", shiftId, 0, new { truckId, startedOffline = true }, at: factory.Clock.GetUtcNow().AddDays(-2)))).Results);
        Assert.Equal("applied", result.Status);
        Assert.Null(result.Code);
        await using var db = Database();
        var shift = await db.Shifts.SingleAsync(Cancellation);
        Assert.Equal("driver_blocked", shift.BlockedAtStart);
        Assert.True(shift.DeviceTimeGap);
        Assert.Equal(new DateOnly(2026, 11, 4), shift.Date);
        Assert.True(await db.Alerts.AnyAsync(alert => alert.RuleId == "A-02" && alert.SubjectId == employeeId && alert.ScopeKey.StartsWith("blocked_shift:"), Cancellation));
    }

    [Fact]
    public async Task Blocked_online_driver_cannot_select_a_truck_but_non_driving_work_saves()
    {
        Guid truckId;
        using (var dispatcher = factory.AsDispatcher()) truckId = (await factory.CreateTruck(dispatcher)).Id;
        using var client = factory.AsDriver(employeeId);
        var refused = Assert.Single((await Batch(client, Command("shift.start", Guid.NewGuid(), 0, new { truckId }))).Results);
        Assert.Equal("failed", refused.Status);
        Assert.Equal("driver_blocked", refused.Code);
        var allowed = Assert.Single((await Batch(client, Command("shift.start", Guid.NewGuid(), 0, new { workWithoutVehicle = true }))).Results);
        Assert.Equal("applied", allowed.Status);
        Assert.Null(allowed.Code);
        await using var db = Database();
        Assert.Null(Assert.Single(await db.Shifts.ToListAsync(Cancellation)).TruckId);
    }

    [Fact]
    public async Task Suspended_employee_cannot_start_even_non_driving_work()
    {
        await using (var setup = Database())
        {
            var employee = await setup.Employees.SingleAsync(row => row.Id == employeeId, Cancellation);
            employee.Status = EmployeeStatus.Suspended;
            await setup.SaveChangesAsync(Cancellation);
        }
        using var client = factory.AsDriver(employeeId);
        var result = Assert.Single((await Batch(client, Command("shift.start", Guid.NewGuid(), 0, new { workWithoutVehicle = true }))).Results);
        Assert.Equal("failed", result.Status);
        Assert.Equal("employee_not_active", result.Code);
        await using var db = Database();
        Assert.False(await db.Shifts.AnyAsync(Cancellation));
        Assert.Equal("failed", Assert.Single(await db.CommandReceipts.ToListAsync(Cancellation)).Outcome);
    }

    [Fact]
    public async Task Driver_snapshot_never_contains_another_drivers_shifts_or_command_receipts()
    {
        using (var own = factory.AsDriver(employeeId)) await Batch(own, Command("shift.start", Guid.NewGuid(), 0, new { workWithoutVehicle = true }));
        var otherId = await Employee();
        var secretId = Guid.NewGuid();
        using (var other = factory.AsDriver(otherId)) await Batch(other, Command("shift.start", secretId, 0, new { workWithoutVehicle = true }));
        using var client = factory.AsDriver(employeeId);
        using var response = await client.GetAsync("/api/v1/driver/shifts", Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = await response.Content.ReadFromJsonAsync<ShiftSnapshot>(VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Single(snapshot!.Shifts);
        Assert.Single(snapshot.AppliedCommandIds);
        Assert.DoesNotContain(secretId.ToString(), await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
    }

    private DriverCommand Command(string type, Guid aggregate, int version, object payload, Guid? id = null, DateTimeOffset? at = null) =>
        new(id ?? Guid.NewGuid(), type, 1, aggregate, version, at ?? factory.Clock.GetUtcNow(), JsonSerializer.SerializeToElement(payload, VehicleApiFactory.JsonOptions));

    [Fact]
    public async Task Truck_resolution_prefers_a_current_swap_then_falls_back_to_the_cycle()
    {
        Guid cycleTruck;
        Guid swapTruck;
        using (var office = factory.AsDispatcher())
        {
            cycleTruck = (await factory.CreateTruck(office)).Id;
            swapTruck = (await factory.CreateTruck(office)).Id;
        }
        var today = new DateOnly(2026, 11, 6);
        await ValidDocuments(cycleTruck);
        await ValidDocuments(swapTruck);
        await using (var setup = Database())
        {
            setup.Cycles.Add(new Cycle { EmployeeId = employeeId, VehicleId = cycleTruck, From = today.AddDays(-3), To = today.AddDays(5), Source = CycleSource.Manual, Status = CycleStatus.Active });
            setup.Assignments.Add(new FleetOps.Modules.Fleet.Assignment { EmployeeId = employeeId, VehicleId = swapTruck, From = today.AddDays(-2), To = today.AddDays(1) });
            await setup.SaveChangesAsync(Cancellation);
        }
        using var driver = factory.AsDriver(employeeId);
        var first = Assert.Single((await Batch(driver, Command("shift.start", Guid.NewGuid(), 0, new { }))).Results);
        Assert.Equal("applied", first.Status);
        Assert.Equal(swapTruck, first.Shift!.TruckId);
        await Batch(driver, Command("shift.end", first.Shift.Id, first.Shift.Version, new { }));
        await using (var setup = Database())
        {
            var assignment = await setup.Assignments.SingleAsync(Cancellation);
            assignment.To = today.AddDays(-1);
            await setup.SaveChangesAsync(Cancellation);
        }
        var second = Assert.Single((await Batch(driver, Command("shift.start", Guid.NewGuid(), 0, new { }))).Results);
        Assert.Equal("applied", second.Status);
        Assert.Equal(cycleTruck, second.Shift!.TruckId);
        await using var db = Database();
        Assert.Equal(cycleTruck, (await db.Shifts.SingleAsync(shift => shift.Status == ShiftStatus.Running, Cancellation)).TruckId);
    }

    [Fact]
    public async Task Offline_readiness_uses_document_validity_at_start_instead_of_arrival()
    {
        Guid truckId;
        using (var office = factory.AsDispatcher()) truckId = (await factory.CreateTruck(office)).Id;
        await ValidDocuments(truckId);
        await using (var setup = Database())
        {
            foreach (var document in await setup.Documents.Where(document => document.OwnerId == employeeId && document.Type == DocumentType.Medical).ToListAsync(Cancellation))
                document.ExpiresAt = new DateOnly(2026, 11, 5);
            await setup.SaveChangesAsync(Cancellation);
        }
        using var client = factory.AsDriver(employeeId);
        var result = Assert.Single((await Batch(client, Command("shift.start", Guid.NewGuid(), 0, new { truckId, startedOffline = true }, at: factory.Clock.GetUtcNow().AddDays(-2)))).Results);
        Assert.Equal("applied", result.Status);
        Assert.Null(result.Shift!.BlockedAtStart);
        await using var db = Database();
        Assert.Null(Assert.Single(await db.Shifts.ToListAsync(Cancellation)).BlockedAtStart);
    }

    [Fact]
    public async Task Overrun_job_and_driver_finish_open_then_clear_A20()
    {
        Guid truckId;
        using (var office = factory.AsDispatcher()) truckId = (await factory.CreateTruck(office)).Id;
        var cycleId = Guid.NewGuid();
        await using (var setup = Database())
        {
            setup.Cycles.Add(new Cycle { Id = cycleId, EmployeeId = employeeId, VehicleId = truckId, From = new(2026, 11, 1), To = new(2026, 11, 5), Status = CycleStatus.Active, Source = CycleSource.Manual });
            await setup.SaveChangesAsync(Cancellation);
        }
        await RunJob("cycles-day");
        await using (var check = Database())
        {
            Assert.Equal(CycleStatus.Overrun, (await check.Cycles.SingleAsync(Cancellation)).Status);
            Assert.True(await check.Alerts.AnyAsync(alert => alert.RuleId == "A-20" && alert.SubjectId == cycleId && alert.Status == FleetOps.Modules.Alerts.AlertStatus.Open, Cancellation));
        }
        using var driver = factory.AsDriver(employeeId);
        var shiftId = Guid.NewGuid();
        await Batch(driver, Command("shift.start", shiftId, 0, new { workWithoutVehicle = true }));
        var finished = Assert.Single((await Batch(driver, Command("shift.end", shiftId, 1, new { finishedCycle = true }))).Results);
        Assert.Equal("applied", finished.Status);
        Assert.Null(finished.Code);
        await using var verify = Database();
        Assert.Equal(CycleStatus.Done, (await verify.Cycles.SingleAsync(Cancellation)).Status);
        Assert.False(await verify.Alerts.AnyAsync(alert => alert.RuleId == "A-20" && alert.SubjectId == cycleId && alert.Status != FleetOps.Modules.Alerts.AlertStatus.Resolved, Cancellation));
    }

    [Fact]
    public async Task Monday_reminder_and_missing_days_exclude_approved_absences_and_do_not_repeat()
    {
        factory.Clock.SetUtcNow(DateTimeOffset.Parse("2026-11-09T08:00:00Z"));
        Guid truckId;
        using (var office = factory.AsDispatcher()) truckId = (await factory.CreateTruck(office)).Id;
        await using (var setup = Database())
        {
            setup.Cycles.Add(new Cycle { EmployeeId = employeeId, VehicleId = truckId, From = new(2026, 11, 2), To = new(2026, 11, 15), Status = CycleStatus.Active, Source = CycleSource.Manual });
            var draft = ShiftRules.Start(Guid.NewGuid(), employeeId, new DateOnly(2026, 11, 8), factory.Clock.GetUtcNow().AddDays(-1), null);
            ShiftRules.End(draft, draft.StartAt.AddHours(8));
            setup.Shifts.Add(draft);
            setup.Absences.Add(new Absence { EmployeeId = employeeId, From = new(2026, 11, 7), To = new(2026, 11, 7), Type = AbsenceType.Sick, Status = AbsenceStatus.Approved });
            await setup.SaveChangesAsync(Cancellation);
        }
        await RunJob("alerts-sweep");
        await RunJob("alerts-sweep");
        await using var db = Database();
        Assert.True(await db.Alerts.AnyAsync(alert => alert.RuleId == "A-15" && alert.SubjectId == employeeId, Cancellation));
        Assert.True(await db.Alerts.AnyAsync(alert => alert.RuleId == "A-09" && alert.SubjectId == employeeId && alert.ScopeKey == "2026-11-06", Cancellation));
        Assert.False(await db.Alerts.AnyAsync(alert => alert.RuleId == "A-09" && alert.SubjectId == employeeId && (alert.ScopeKey == "2026-11-07" || alert.ScopeKey == "2026-11-08"), Cancellation));
        Assert.Equal(1, await db.Notifications.CountAsync(notification => notification.EmployeeId == employeeId && notification.TemplateCode == "week_reminder", Cancellation));
    }

    [Fact]
    public async Task Off_week_sends_no_week_reminder_or_missing_day_alert()
    {
        factory.Clock.SetUtcNow(DateTimeOffset.Parse("2026-11-09T08:00:00Z"));
        Guid truckId;
        using (var office = factory.AsDispatcher()) truckId = (await factory.CreateTruck(office)).Id;
        await using (var setup = Database())
        {
            setup.Cycles.Add(new Cycle { EmployeeId = employeeId, VehicleId = truckId, From = new(2026, 11, 2), To = new(2026, 11, 8), Status = CycleStatus.Active, Source = CycleSource.Manual });
            var draft = ShiftRules.Start(Guid.NewGuid(), employeeId, new DateOnly(2026, 11, 8), factory.Clock.GetUtcNow().AddDays(-1), null);
            ShiftRules.End(draft, draft.StartAt.AddHours(8));
            setup.Shifts.Add(draft);
            await setup.SaveChangesAsync(Cancellation);
        }
        await RunJob("alerts-sweep");
        await using var db = Database();
        Assert.False(await db.Alerts.AnyAsync(alert => alert.SubjectId == employeeId && (alert.RuleId == "A-09" || alert.RuleId == "A-15"), Cancellation));
        Assert.False(await db.Notifications.AnyAsync(notification => notification.EmployeeId == employeeId && notification.TemplateCode == "week_reminder", Cancellation));
    }

    private async Task RunJob(string name)
    {
        factory.Services.GetRequiredService<IConfiguration>()["Jobs:Secret"] = "epic-four-test-only";
        using var client = factory.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "epic-four-test-only");
        using var response = await client.GetAsync($"/jobs/{name}", Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
    }

    private async Task ValidDocuments(Guid truckId)
    {
        await using var db = Database();
        foreach (var catalog in DocumentsCatalog.All.Where(catalog => catalog.Blocking))
        {
            var owner = catalog.OwnerType == DocumentOwnerType.Employee ? employeeId : truckId;
            if (await db.Documents.AnyAsync(document => document.OwnerId == owner && document.Type == catalog.Type, Cancellation)) continue;
            db.Documents.Add(new Document { OwnerId = owner, OwnerType = catalog.OwnerType, Type = catalog.Type, Status = DocumentStatus.Verified,
                ExpiresAt = new DateOnly(2027, 11, 6), VerifiedAt = factory.Clock.GetUtcNow().AddDays(-20), CreatedAt = factory.Clock.GetUtcNow().AddDays(-20),
                CreatedBy = VehicleApiFactory.ActorId, ImportAttested = true, Categories = catalog.Type == DocumentType.LicenceC_CE ? ["C", "CE"] : [] });
        }
        await db.SaveChangesAsync(Cancellation);
    }

    [Fact]
    public async Task Database_partial_index_refuses_second_running_shift_outside_command_pipeline()
    {
        var shiftId = Guid.NewGuid();
        using var client = factory.AsDriver(employeeId);
        await Batch(client, Command("shift.start", shiftId, 0, new { workWithoutVehicle = true }));
        await using (var write = Database())
        {
            write.Shifts.Add(ShiftRules.Start(Guid.NewGuid(), employeeId, new DateOnly(2026, 11, 6), factory.Clock.GetUtcNow(), null));
            var error = await Assert.ThrowsAsync<DbUpdateException>(() => write.SaveChangesAsync(Cancellation));
            var postgres = Assert.IsType<PostgresException>(error.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, postgres.SqlState);
            Assert.Equal("ux_shifts_one_running_per_employee", postgres.ConstraintName);
        }
        await using var verify = Database();
        Assert.Equal(shiftId, Assert.Single(await verify.Shifts.ToListAsync(Cancellation)).Id);
    }

    [Fact]
    public async Task Driver_cannot_claim_an_existing_shift_by_reusing_its_aggregate_id()
    {
        var shiftId = Guid.NewGuid();
        using (var owner = factory.AsDriver(employeeId))
            await Batch(owner, Command("shift.start", shiftId, 0, new { workWithoutVehicle = true }));
        var otherId = await Employee();
        using var other = factory.AsDriver(otherId);
        var result = Assert.Single((await Batch(other, Command("shift.start", shiftId, 0, new { workWithoutVehicle = true }))).Results);
        Assert.Equal("failed", result.Status);
        Assert.Equal("shift_not_found", result.Code);
        await using var db = Database();
        var shift = Assert.Single(await db.Shifts.ToListAsync(Cancellation));
        Assert.Equal(employeeId, shift.EmployeeId);
        Assert.Equal(1, shift.Version);
        Assert.Equal(1, await db.CommandReceipts.CountAsync(Cancellation));
    }

    private async Task<DriverCommandBatchResult> Batch(HttpClient client, params DriverCommand[] commands)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/driver/commands/batch", new DriverCommandBatch(commands), VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DriverCommandBatchResult>(VehicleApiFactory.JsonOptions, Cancellation))!;
    }

    private async Task<Guid> Employee()
    {
        var id = Guid.NewGuid();
        await using var db = Database();
        db.Employees.Add(new Employee { Id = id, FirstName = "Shift", LastName = "Fake", Email = $"{id:N}@example.invalid", Roles = "[\"driver\"]", StartDate = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync(Cancellation);
        return id;
    }

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "shift_sync_setup" };
    }
}
