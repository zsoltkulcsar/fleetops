using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Maintenance;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public sealed class MaintenanceTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private static readonly DateOnly Today = new(2026, 11, 6);
    private Guid truckId;
    private Guid driverId;
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        await ReadinessTestSupport.AddEmployee(factory, VehicleApiFactory.ActorId, "[\"dispatcher\"]");
        driverId = Guid.NewGuid();
        await ReadinessTestSupport.AddEmployee(factory, driverId, "[\"driver\"]");
        using var office = factory.AsDispatcher();
        truckId = (await factory.CreateTruck(office)).Id;
        await ReadinessTestSupport.AddAllVehicleDocuments(factory, truckId);
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Maintenance_list_derives_overdue_and_shows_km_as_of()
    {
        await Odometer(1600, factory.Clock.GetUtcNow());
        var dateDue = await Plan(dueDate: Today.AddDays(-1));
        var kmDue = await Plan(dueKm: 1500);
        var future = await Plan(dueDate: Today.AddDays(1), dueKm: 1700);
        var list = await Get("/api/v1/fleet/maintenance?overdue=true");
        var items = list.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Contains(items, item => item.GetProperty("id").GetGuid() == dateDue);
        Assert.Contains(items, item => item.GetProperty("id").GetGuid() == kmDue);
        Assert.DoesNotContain(items, item => item.GetProperty("id").GetGuid() == future);
        Assert.All(items, item =>
        {
            Assert.True(item.GetProperty("overdue").GetBoolean());
            Assert.Equal(1600, item.GetProperty("odometerKm").GetInt32());
            Assert.Equal(factory.Clock.GetUtcNow(), item.GetProperty("kmAsOf").GetDateTimeOffset());
        });
        await using var db = Database();
        Assert.Equal(3, await db.Maintenance.CountAsync(Cancellation));
        Assert.Equal(MaintenanceStatus.Scheduled, (await db.Maintenance.SingleAsync(item => item.Id == dateDue, Cancellation)).Status);
    }

    [Fact]
    public async Task Plan_needs_a_due_date_or_due_km()
    {
        using var office = factory.AsDispatcher();
        using var refused = await office.PostAsJsonAsync("/api/v1/fleet/maintenance", PlanBody(), Cancellation);
        await Error(refused, HttpStatusCode.BadRequest, "validation_error");
        await using (var db = Database())
            Assert.False(await db.Maintenance.AnyAsync(Cancellation));
        var dateOnly = await Plan(dueDate: Today);
        var kmOnly = await Plan(dueKm: 2000);
        await using var verify = Database();
        Assert.Equal(Today, (await verify.Maintenance.SingleAsync(item => item.Id == dateOnly, Cancellation)).DueDate);
        Assert.Equal(2000, (await verify.Maintenance.SingleAsync(item => item.Id == kmOnly, Cancellation)).DueKm);
    }

    [Fact]
    public async Task A06_turns_critical_past_the_set_days_or_km()
    {
        using (var owner = factory.AsOwner())
        using (var response = await owner.PutAsJsonAsync("/api/v1/settings", new Dictionary<string, int> { ["alerts.a06.critical_days"] = 3, ["alerts.a06.critical_km"] = 100 }, Cancellation))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Odometer(1500, factory.Clock.GetUtcNow());
        var byDate = await Plan(dueDate: Today.AddDays(-3));
        var byKm = await Plan(dueKm: 1400);
        await Sweep();
        await AssertAlert("A-06", truckId, byDate.ToString(), AlertStatus.Open, AlertSeverity.Warning);
        await AssertAlert("A-06", truckId, byKm.ToString(), AlertStatus.Open, AlertSeverity.Warning);
        factory.Clock.Advance(TimeSpan.FromDays(1));
        await Odometer(1501, factory.Clock.GetUtcNow());
        await Sweep();
        await AssertAlert("A-06", truckId, byDate.ToString(), AlertStatus.Open, AlertSeverity.Critical);
        await AssertAlert("A-06", truckId, byKm.ToString(), AlertStatus.Open, AlertSeverity.Critical);
    }

    [Fact]
    public async Task In_progress_shows_the_vehicle_in_workshop()
    {
        var repair = await Plan(dueDate: Today);
        var started = await Post($"/api/v1/fleet/maintenance/{repair}/start", new { });
        Assert.True(started.GetProperty("inWorkshop").GetBoolean());
        var vehicle = await Get($"/api/v1/fleet/vehicles/{truckId}");
        Assert.Equal("in_maintenance", vehicle.GetProperty("shownStatus").GetString());
        Assert.False(vehicle.GetProperty("usable").GetBoolean());
        await using var db = Database();
        Assert.Equal(MaintenanceStatus.InProgress, (await db.Maintenance.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task Done_with_a_result_document_makes_it_current_and_closes_linked_defects()
    {
        var defect = await Defect();
        var repair = await Plan(dueDate: Today, type: "inspection_hu", defects: [defect]);
        Guid oldDocument;
        await using (var setup = Database())
            oldDocument = (await setup.Documents.SingleAsync(item => item.OwnerId == truckId && item.Type == DocumentType.HuReport, Cancellation)).Id;
        var upload = await Post("/api/v1/fleet/maintenance/documents/upload", new { vehicleId = truckId, type = "hu_report", contentType = "application/pdf", sizeBytes = 12 });
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "application/pdf");
        var fileId = upload.GetProperty("fileId").GetGuid();
        var done = await Post($"/api/v1/fleet/maintenance/{repair}/done", new
        {
            doneAt = Today,
            costEur = 125.50m,
            resultDocument = new { type = "hu_report", issuedAt = Today, expiresAt = Today.AddYears(1), number = "HU-TEST", files = new[] { new { fileId, page = "page" } } }
        });
        var documentId = done.GetProperty("resultDocumentId").GetGuid();
        await using var db = Database();
        var document = await db.Documents.Include(item => item.Files).SingleAsync(item => item.Id == documentId, Cancellation);
        Assert.Equal(DocumentStatus.Verified, document.Status);
        Assert.Equal(truckId, document.OwnerId);
        Assert.Equal(Today.AddYears(1), document.ExpiresAt);
        Assert.Equal(VehicleApiFactory.ActorId, document.VerifiedBy);
        Assert.Single(document.Files);
        Assert.Equal(DocumentStatus.Replaced, (await db.Documents.SingleAsync(item => item.Id == oldDocument, Cancellation)).Status);
        Assert.Equal(DefectStatus.Closed, (await db.DefectReports.SingleAsync(item => item.Id == defect, Cancellation)).Status);
        var saved = await db.Maintenance.SingleAsync(Cancellation);
        Assert.Equal(MaintenanceStatus.Done, saved.Status);
        Assert.Equal(documentId, saved.ResultDocumentId);
        Assert.Equal(125.50m, saved.CostEur);
        Assert.NotNull((await db.PendingFiles.SingleAsync(item => item.Id == fileId, Cancellation)).ConfirmedAt);
    }

    [Fact]
    public async Task Done_ends_a_swap_tied_to_the_repair()
    {
        var repair = await Plan(dueDate: Today);
        using var office = factory.AsDispatcher();
        var replacement = (await factory.CreateTruck(office)).Id;
        await ReadinessTestSupport.AddAllVehicleDocuments(factory, replacement);
        var swap = await Post("/api/v1/fleet/assignments", new { employeeId = driverId, vehicleId = replacement, maintenanceId = repair });
        await Post($"/api/v1/fleet/maintenance/{repair}/done", new { doneAt = Today });
        await using var db = Database();
        var assignment = await db.Assignments.SingleAsync(item => item.Id == swap.GetProperty("id").GetGuid(), Cancellation);
        Assert.Equal(repair, assignment.MaintenanceId);
        Assert.Equal(Today, assignment.To);
        Assert.Equal(MaintenanceStatus.Done, (await db.Maintenance.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task Cancel_needs_a_reason_and_reopens_linked_defects()
    {
        var defect = await Defect();
        var repair = await Plan(dueDate: Today, defects: [defect]);
        using (var office = factory.AsDispatcher())
        using (var response = await office.PostAsJsonAsync($"/api/v1/fleet/maintenance/{repair}/cancel", new { reason = " " }, Cancellation))
            await Error(response, HttpStatusCode.BadRequest, "validation_error");
        await using (var before = Database())
        {
            Assert.Equal(MaintenanceStatus.Scheduled, (await before.Maintenance.SingleAsync(Cancellation)).Status);
            Assert.Equal(DefectStatus.InRepair, (await before.DefectReports.SingleAsync(Cancellation)).Status);
        }
        await Post($"/api/v1/fleet/maintenance/{repair}/cancel", new { reason = "Workshop unavailable" });
        await using var db = Database();
        var saved = await db.Maintenance.SingleAsync(Cancellation);
        Assert.Equal(MaintenanceStatus.Cancelled, saved.Status);
        Assert.Equal("Workshop unavailable", saved.CancelReason);
        var report = await db.DefectReports.SingleAsync(Cancellation);
        Assert.Equal(DefectStatus.Open, report.Status);
        Assert.Null(report.RepairId);
    }

    [Fact]
    public async Task Done_service_proposes_the_next_from_the_interval()
    {
        var repair = await Plan(dueDate: Today, type: "service");
        var done = await Post($"/api/v1/fleet/maintenance/{repair}/done", new { doneAt = Today, odometerAtDoneKm = 120000 });
        var proposal = done.GetProperty("nextService");
        Assert.Equal(truckId, proposal.GetProperty("vehicleId").GetGuid());
        Assert.Equal("service", proposal.GetProperty("type").GetString());
        Assert.Equal("2027-11-06", proposal.GetProperty("dueDate").GetString());
        Assert.Equal(170000, proposal.GetProperty("dueKm").GetInt32());
        await using var db = Database();
        var saved = Assert.Single(await db.Maintenance.ToListAsync(Cancellation));
        Assert.Equal(MaintenanceStatus.Done, saved.Status);
        Assert.Equal(120000, saved.OdometerAtDoneKm);
        Assert.Equal(Today, saved.DoneAt);
    }

    [Fact]
    public async Task Old_odometer_falls_back_to_the_month_interval()
    {
        var measured = factory.Clock.GetUtcNow().AddDays(-31);
        await Odometer(200000, measured);
        var future = await Plan(dueDate: Today.AddMonths(1), dueKm: 150000, type: "service");
        var expired = await Plan(dueDate: Today.AddDays(-1), dueKm: 150000, type: "service");
        var items = (await Get("/api/v1/fleet/maintenance")).GetProperty("items").EnumerateArray().ToArray();
        var waiting = Assert.Single(items, item => item.GetProperty("id").GetGuid() == future);
        Assert.False(waiting.GetProperty("overdue").GetBoolean());
        Assert.True(waiting.GetProperty("kmReadingStale").GetBoolean());
        Assert.Equal("km reading older than 30 days", waiting.GetProperty("kmNote").GetString());
        Assert.True(Assert.Single(items, item => item.GetProperty("id").GetGuid() == expired).GetProperty("overdue").GetBoolean());
        await Sweep();
        await using var db = Database();
        Assert.False(await db.Alerts.AnyAsync(item => item.RuleId == "A-06" && item.ScopeKey == future.ToString() && item.Status == AlertStatus.Open, Cancellation));
        Assert.True(await db.Alerts.AnyAsync(item => item.RuleId == "A-06" && item.ScopeKey == expired.ToString() && item.Status == AlertStatus.Open, Cancellation));
        Assert.Equal(measured, (await db.Vehicles.SingleAsync(item => item.Id == truckId, Cancellation)).OdometerUpdatedAt);
    }

    [Fact]
    public async Task Shift_odometer_updates_the_vehicle_and_ignores_lower_readings()
    {
        await ReadinessTestSupport.AddAllDriverDocuments(factory, driverId);
        await using (var setup = Database())
        {
            foreach (var document in await setup.Documents.Where(item => item.OwnerId == driverId).ToListAsync(Cancellation))
            {
                document.CreatedAt = factory.Clock.GetUtcNow().AddDays(-10);
                document.VerifiedAt = factory.Clock.GetUtcNow().AddDays(-10);
            }
            await setup.SaveChangesAsync(Cancellation);
        }
        await Odometer(1000, factory.Clock.GetUtcNow().AddDays(-2));
        await EndShift(1200);
        var updatedAt = factory.Clock.GetUtcNow();
        await using (var first = Database())
        {
            var vehicle = await first.Vehicles.SingleAsync(item => item.Id == truckId, Cancellation);
            Assert.Equal(1200, vehicle.OdometerKm);
            Assert.Equal(updatedAt, vehicle.OdometerUpdatedAt);
        }
        factory.Clock.Advance(TimeSpan.FromDays(1));
        await EndShift(1100);
        await using var db = Database();
        var saved = await db.Vehicles.SingleAsync(item => item.Id == truckId, Cancellation);
        Assert.Equal(1200, saved.OdometerKm);
        Assert.Equal(updatedAt, saved.OdometerUpdatedAt);
        var shifts = await db.Shifts.OrderBy(item => item.StartAt).ToListAsync(Cancellation);
        Assert.Equal(2, shifts.Count);
        Assert.Equal(1200, shifts[0].OdometerEndKm);
        Assert.False(shifts[0].OdometerLower);
        Assert.Equal(1100, shifts[1].OdometerEndKm);
        Assert.True(shifts[1].OdometerLower);
    }

    [Fact]
    public async Task Maintenance_in_a_cycles_first_week_opens_a21_and_moving_it_clears()
    {
        var cycle = new Cycle { EmployeeId = driverId, VehicleId = truckId, From = Today.AddDays(2), To = Today.AddDays(20), Status = CycleStatus.Planned, Source = CycleSource.Manual };
        await using (var setup = Database())
        {
            setup.Cycles.Add(cycle);
            await setup.SaveChangesAsync(Cancellation);
        }
        var repair = await Plan(dueDate: Today.AddDays(30), scheduledFor: cycle.From.AddDays(6));
        await Sweep();
        await AssertAlert("A-21", cycle.Id, "clash", AlertStatus.Open, AlertSeverity.Warning);
        using (var office = factory.AsDispatcher())
        using (var response = await office.PutAsJsonAsync($"/api/v1/fleet/maintenance/{repair}", new { scheduledFor = cycle.From.AddDays(7), workshop = "Central", dueDate = Today.AddDays(30) }, Cancellation))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Sweep();
        await AssertAlert("A-21", cycle.Id, "clash", AlertStatus.Resolved, AlertSeverity.Warning);
        await using var db = Database();
        Assert.Equal(cycle.From.AddDays(7), (await db.Maintenance.SingleAsync(Cancellation)).ScheduledFor);
    }

    [Fact]
    public async Task A12_opens_past_the_interval_and_logging_clears_it()
    {
        await using (var setup = Database())
        {
            (await setup.Vehicles.SingleAsync(item => item.Id == truckId, Cancellation)).LastTachoDownloadAt = Today.AddDays(-90);
            (await setup.Employees.SingleAsync(item => item.Id == driverId, Cancellation)).LastTachoDownloadAt = Today.AddDays(-28);
            await setup.SaveChangesAsync(Cancellation);
        }
        await Sweep();
        await using (var boundary = Database())
            Assert.False(await boundary.Alerts.AnyAsync(item => item.RuleId == "A-12" && (item.SubjectId == truckId || item.SubjectId == driverId) && item.Status == AlertStatus.Open, Cancellation));
        factory.Clock.Advance(TimeSpan.FromDays(1));
        await Sweep();
        await AssertAlert("A-12", truckId, "download_due", AlertStatus.Open, AlertSeverity.Warning);
        await AssertAlert("A-12", driverId, "download_due", AlertStatus.Open, AlertSeverity.Warning);
        var date = Today.AddDays(1);
        await Post($"/api/v1/fleet/vehicles/{truckId}/tacho-downloads", new { date });
        await Post($"/api/v1/people/{driverId}/tacho-downloads", new { date });
        await AssertAlert("A-12", truckId, "download_due", AlertStatus.Resolved, AlertSeverity.Warning);
        await AssertAlert("A-12", driverId, "download_due", AlertStatus.Resolved, AlertSeverity.Warning);
        await using var db = Database();
        Assert.Equal(date, (await db.Vehicles.SingleAsync(item => item.Id == truckId, Cancellation)).LastTachoDownloadAt);
        Assert.Equal(date, (await db.Employees.SingleAsync(item => item.Id == driverId, Cancellation)).LastTachoDownloadAt);
    }

    [Fact]
    public async Task Hr_and_drivers_cannot_manage_maintenance()
    {
        var repair = await Plan(dueDate: Today);
        foreach (var driver in new[] { false, true })
        {
            using var client = driver ? factory.AsDriver(driverId) : factory.AsHr();
            using (var read = await client.GetAsync("/api/v1/fleet/maintenance", Cancellation))
                await Error(read, HttpStatusCode.Forbidden, "permission_denied");
            foreach (var write in new (string Path, object Body)[]
            {
                ("/api/v1/fleet/maintenance", PlanBody(Today)),
                ($"/api/v1/fleet/maintenance/{repair}/start", new { }),
                ($"/api/v1/fleet/maintenance/{repair}/done", new { doneAt = Today }),
                ($"/api/v1/fleet/maintenance/{repair}/cancel", new { reason = "Valid reason" }),
                ("/api/v1/fleet/maintenance/documents/upload", new { vehicleId = truckId, type = "hu_report", contentType = "application/pdf", sizeBytes = 12 })
            })
            {
                using var response = await client.PostAsJsonAsync(write.Path, write.Body, Cancellation);
                await Error(response, HttpStatusCode.Forbidden, "permission_denied");
            }
        }
        await using var db = Database();
        Assert.Equal(MaintenanceStatus.Scheduled, (await db.Maintenance.SingleAsync(Cancellation)).Status);
        Assert.False(await db.PendingFiles.AnyAsync(Cancellation));
    }

    private object PlanBody(DateOnly? dueDate = null, int? dueKm = null, string type = "repair", Guid[]? defects = null, DateOnly? scheduledFor = null) =>
        new { vehicleId = truckId, type, workshop = "Central", scheduledFor = scheduledFor ?? Today, dueDate, dueKm, defectReportIds = defects ?? [] };

    private async Task<Guid> Plan(DateOnly? dueDate = null, int? dueKm = null, string type = "repair", Guid[]? defects = null, DateOnly? scheduledFor = null) =>
        (await Post("/api/v1/fleet/maintenance", PlanBody(dueDate, dueKm, type, defects, scheduledFor))).GetProperty("id").GetGuid();

    private async Task<Guid> Defect() =>
        (await Post("/api/v1/fleet/defects", new { vehicleId = truckId, category = "brakes", severity = "minor", description = "Brake noise", reportedBy = driverId })).GetProperty("id").GetGuid();

    private async Task Odometer(int km, DateTimeOffset measured)
    {
        await using var db = Database();
        var vehicle = await db.Vehicles.SingleAsync(item => item.Id == truckId, Cancellation);
        vehicle.OdometerKm = km;
        vehicle.OdometerUpdatedAt = measured;
        await db.SaveChangesAsync(Cancellation);
    }

    private async Task EndShift(int km)
    {
        using var driver = factory.AsDriver(driverId);
        var id = Guid.NewGuid();
        foreach (var command in new[]
        {
            new DriverCommand(Guid.NewGuid(), "shift.start", 1, id, 0, factory.Clock.GetUtcNow().AddHours(-1), JsonSerializer.SerializeToElement(new { truckId }, VehicleApiFactory.JsonOptions)),
            new DriverCommand(Guid.NewGuid(), "shift.end", 1, id, 1, factory.Clock.GetUtcNow(), JsonSerializer.SerializeToElement(new { odometerKm = km }, VehicleApiFactory.JsonOptions))
        })
        {
            using var response = await driver.PostAsJsonAsync("/api/v1/driver/commands/batch", new DriverCommandBatch([command]), VehicleApiFactory.JsonOptions, Cancellation);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var batch = await response.Content.ReadFromJsonAsync<DriverCommandBatchResult>(VehicleApiFactory.JsonOptions, Cancellation);
            var result = Assert.Single(batch!.Results);
            Assert.Equal("applied", result.Status);
            Assert.Null(result.Code);
        }
    }

    private async Task AssertAlert(string rule, Guid subject, string scope, AlertStatus status, AlertSeverity severity)
    {
        await using var db = Database();
        var alert = await db.Alerts.SingleAsync(item => item.RuleId == rule && item.SubjectId == subject && item.ScopeKey == scope, Cancellation);
        Assert.Equal(status, alert.Status);
        Assert.Equal(severity, alert.Severity);
    }

    private async Task Sweep()
    {
        factory.Services.GetRequiredService<IConfiguration>()["Jobs:Secret"] = "maintenance-test-only";
        using var client = factory.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "maintenance-test-only");
        using var response = await client.GetAsync("/jobs/alerts-sweep", Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("succeeded", body.GetProperty("status").GetString());
    }

    private async Task<JsonElement> Post(string path, object body)
    {
        using var office = factory.AsDispatcher();
        using var response = await office.PostAsJsonAsync(path, body, VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
    }

    private async Task<JsonElement> Get(string path)
    {
        using var office = factory.AsDispatcher();
        using var response = await office.GetAsync(path, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
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
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "maintenance_test_setup" };
    }
}



