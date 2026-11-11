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

public sealed class DefectTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private Guid reporterId;
    private Guid truckId;
    private static readonly DateOnly Today = new(2026, 11, 6);
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        reporterId = await Employee();
        await ReadinessTestSupport.AddEmployee(factory, VehicleApiFactory.ActorId, "[\"dispatcher\"]");
        truckId = await Truck();
    }

    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Driver_report_saves_an_app_defect_on_any_vehicle()
    {
        var otherTruck = await Truck();
        await Cycle(reporterId, truckId);
        var id = await Report(vehicle: otherTruck, severity: "minor");
        await using var db = Database();
        var saved = await db.DefectReports.SingleAsync(Cancellation);
        Assert.Equal(id, saved.Id);
        Assert.Equal(otherTruck, saved.VehicleId);
        Assert.Equal(reporterId, saved.ReportedBy);
        Assert.Equal(DefectOrigin.App, saved.Origin);
        Assert.Equal(DefectSeverity.Minor, saved.Severity);
        Assert.Equal(DefectCategory.Brakes, saved.Category);
        Assert.Equal("Brake noise", saved.Description);
        Assert.Equal(DefectStatus.Open, saved.Status);
    }

    [Fact]
    public async Task Report_needs_a_photo_or_a_description()
    {
        var result = await Apply(Command("defect.report", Guid.NewGuid(), new { vehicleId = truckId, category = "brakes", severity = "minor", description = " " }));
        Assert.Equal("failed", result.Status);
        Assert.Equal("invalid_payload", result.Code);
        await using var db = Database();
        Assert.False(await db.DefectReports.AnyAsync(Cancellation));
        var accepted = await Report(severity: "minor");
        Assert.Equal(accepted, (await db.DefectReports.SingleAsync(Cancellation)).Id);
    }

    [Fact]
    public async Task Queued_report_with_photos_replays_once_and_links_the_files()
    {
        var deviceTime = factory.Clock.GetUtcNow().AddHours(-2);
        var photo = await Photo();
        var secondPhoto = await Photo();
        var id = Guid.NewGuid();
        var command = Command("defect.report", id, new { vehicleId = truckId, category = "brakes", severity = "major", photos = new[] { photo, secondPhoto } }, deviceTime);
        var first = await Apply(command);
        var replay = await Apply(command);
        Assert.Equal("applied", first.Status);
        Assert.Null(first.Code);
        Assert.Equal(first, replay);
        await using var db = Database();
        var saved = await db.DefectReports.SingleAsync(Cancellation);
        Assert.Equal(id, saved.Id);
        Assert.Equal(new[] { photo, secondPhoto }, saved.Photos);
        Assert.Equal(command.DeviceTime, saved.DeviceTime);
        Assert.Equal(factory.Clock.GetUtcNow(), saved.ServerReceivedAt);
        var linked = await db.DefectPhotos.OrderBy(row => row.SortOrder).ToListAsync(Cancellation);
        Assert.Equal(new[] { photo, secondPhoto }, linked.Select(row => row.Id));
        Assert.All(linked, row =>
        {
            Assert.Equal(id, row.DefectId);
            Assert.Equal(12, row.SizeBytes);
        });
        var pending = await db.PendingFiles.ToListAsync(Cancellation);
        Assert.Equal(2, pending.Count);
        Assert.All(pending, row => Assert.NotNull(row.ConfirmedAt));
        Assert.Equal(1, await db.CommandReceipts.CountAsync(Cancellation));
        using var driver = factory.AsDriver(reporterId);
        var link = await Get(driver, $"/api/v1/driver/defects/{id}/photos/{photo}/link");
        Assert.False(string.IsNullOrWhiteSpace(link.GetProperty("url").GetString()));
        var secondLink = await Get(driver, $"/api/v1/driver/defects/{id}/photos/{secondPhoto}/link");
        Assert.False(string.IsNullOrWhiteSpace(secondLink.GetProperty("url").GetString()));
        Assert.NotEqual(link.GetProperty("url").GetString(), secondLink.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Critical_report_sets_the_vehicle_off_road_and_opens_A07_that_cannot_be_snoozed()
    {
        await Usable(true);
        var id = await Report();
        await Usable(false);
        await using var db = Database();
        var alert = await db.Alerts.SingleAsync(row => row.RuleId == "A-07" && row.SubjectId == id, Cancellation);
        Assert.Equal(AlertStatus.Open, alert.Status);
        using var office = factory.AsDispatcher();
        using var response = await office.PostAsJsonAsync($"/api/v1/alerts/{alert.Id}/snooze", new { version = alert.Version, until = factory.Clock.GetUtcNow().AddDays(1), reason = "Waiting for repair" }, Cancellation);
        await Error(response, HttpStatusCode.Conflict, "not_snoozable");
        await using var verify = Database();
        Assert.Equal(AlertStatus.Open, (await verify.Alerts.SingleAsync(row => row.Id == alert.Id, Cancellation)).Status);
        Assert.Equal(DefectSeverity.Critical, (await verify.DefectReports.SingleAsync(Cancellation)).Severity);
    }

    [Fact]
    public async Task Late_critical_report_still_applies_on_arrival()
    {
        var deviceTime = factory.Clock.GetUtcNow().AddDays(-5);
        var id = await Report(at: deviceTime);
        await Usable(false);
        await using var db = Database();
        var saved = await db.DefectReports.SingleAsync(Cancellation);
        Assert.Equal(deviceTime, saved.ReportedAt);
        Assert.Equal(factory.Clock.GetUtcNow(), saved.ServerReceivedAt);
        Assert.True(await db.Alerts.AnyAsync(row => row.RuleId == "A-07" && row.SubjectId == id && row.Status == AlertStatus.Open, Cancellation));
    }

    [Fact]
    public async Task Truck_stays_off_road_until_both_the_critical_defect_and_the_expired_document_are_fixed()
    {
        await Usable(true);
        var id = await Report();
        await Usable(false);
        await using (var setup = Database())
        {
            var document = await setup.Documents.SingleAsync(row => row.OwnerId == truckId && row.Type == DocumentType.HuReport, Cancellation);
            document.ExpiresAt = Today.AddDays(-1);
            await setup.SaveChangesAsync(Cancellation);
        }
        await Usable(false);
        await PostOffice($"/api/v1/fleet/defects/{id}/close", new { reason = "Repaired on site" });
        await Usable(false);
        await using (var setup = Database())
        {
            var document = await setup.Documents.SingleAsync(row => row.OwnerId == truckId && row.Type == DocumentType.HuReport, Cancellation);
            document.ExpiresAt = Today.AddYears(1);
            await setup.SaveChangesAsync(Cancellation);
        }
        await Usable(true);
        await using var db = Database();
        Assert.Equal(DefectStatus.Closed, (await db.DefectReports.SingleAsync(Cancellation)).Status);
        Assert.Equal(Today.AddYears(1), (await db.Documents.SingleAsync(row => row.OwnerId == truckId && row.Type == DocumentType.HuReport, Cancellation)).ExpiresAt);
    }

    [Fact]
    public async Task Drivers_on_the_truck_get_vehicle_unusable_once()
    {
        var affected = await Employee();
        var secondAffected = await Employee();
        var unrelated = await Employee();
        await Cycle(reporterId, truckId);
        await Cycle(affected, truckId);
        await Cycle(secondAffected, truckId);
        await Cycle(unrelated, await Truck());
        var command = Command("defect.report", Guid.NewGuid(), new { vehicleId = truckId, category = "brakes", severity = "critical", description = "Unsafe brakes" });
        Assert.Equal("applied", (await Apply(command)).Status);
        Assert.Equal("applied", (await Apply(command)).Status);
        await Report();
        await using var db = Database();
        var notifications = await db.Notifications.Where(row => row.TemplateCode == "vehicle_unusable").ToListAsync(Cancellation);
        Assert.Equal(2, notifications.Count);
        Assert.Single(notifications, row => row.EmployeeId == affected);
        Assert.Single(notifications, row => row.EmployeeId == secondAffected);
        Assert.Equal(2, await db.DefectReports.CountAsync(Cancellation));
    }

    [Fact]
    public async Task Truck_drawer_shows_open_defects_without_the_reporters_name()
    {
        var viewer = await Employee();
        await Cycle(viewer, truckId);
        var open = await Report(severity: "major");
        var closed = await Report(severity: "minor");
        await PostOffice($"/api/v1/fleet/defects/{closed}/close", new { reason = "Checked" });
        using var driver = factory.AsDriver(viewer);
        var body = await Get(driver, $"/api/v1/driver/vehicles/{truckId}");
        var defect = Assert.Single(body.GetProperty("defects").EnumerateArray());
        Assert.Equal(open, defect.GetProperty("id").GetGuid());
        Assert.Equal("major", defect.GetProperty("severity").GetString());
        Assert.DoesNotContain(defect.EnumerateObject(), property => property.Name.Contains("report", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Reporter", body.GetRawText());
        Assert.DoesNotContain(reporterId.ToString(), defect.GetRawText());
        await using var db = Database();
        Assert.Equal(reporterId, (await db.DefectReports.SingleAsync(row => row.Id == open, Cancellation)).ReportedBy);
    }

    [Fact]
    public async Task Cancel_within_60_seconds_by_device_time_reverses_the_critical_and_tells_the_dispatcher()
    {
        var at = factory.Clock.GetUtcNow();
        var id = await Report(at: at);
        factory.Clock.Advance(TimeSpan.FromHours(4));
        var result = await Apply(Command("defect.cancel", id, new { }, at.AddSeconds(60)));
        Assert.Equal("applied", result.Status);
        Assert.Null(result.Code);
        await Usable(true);
        await using var db = Database();
        var saved = await db.DefectReports.SingleAsync(Cancellation);
        Assert.Equal(DefectStatus.Cancelled, saved.Status);
        Assert.Equal(reporterId, saved.ClosedBy);
        Assert.Equal(factory.Clock.GetUtcNow(), saved.ClosedAt);
        Assert.Equal(VehicleApiFactory.ActorId, Assert.Single(await db.Notifications.Where(row => row.TemplateCode == "defect_cancelled").ToListAsync(Cancellation)).EmployeeId);
        Assert.Equal(AlertStatus.Resolved, (await db.Alerts.SingleAsync(row => row.RuleId == "A-07" && row.SubjectId == id, Cancellation)).Status);
    }

    [Fact]
    public async Task Cancel_after_60_seconds_by_device_time_is_refused()
    {
        var at = factory.Clock.GetUtcNow();
        var id = await Report(at: at);
        var result = await Apply(Command("defect.cancel", id, new { }, at.AddSeconds(61)));
        Assert.Equal("failed", result.Status);
        Assert.Equal("cancel_window_passed", result.Code);
        await using var db = Database();
        Assert.Equal(DefectStatus.Open, (await db.DefectReports.SingleAsync(Cancellation)).Status);
        Assert.False(await db.Notifications.AnyAsync(row => row.TemplateCode == "defect_cancelled", Cancellation));
        await Usable(false);
    }

    [Fact]
    public async Task Only_the_reporter_can_cancel()
    {
        var at = factory.Clock.GetUtcNow();
        var id = await Report(at: at);
        var other = await Employee();
        var result = await Apply(Command("defect.cancel", id, new { }, at.AddSeconds(30)), other);
        Assert.Equal("failed", result.Status);
        Assert.Equal("not_cancellable", result.Code);
        await using var db = Database();
        Assert.Equal(DefectStatus.Open, (await db.DefectReports.SingleAsync(Cancellation)).Status);
        Assert.Null((await db.DefectReports.SingleAsync(Cancellation)).ClosedBy);
        var accepted = await Apply(Command("defect.cancel", id, new { }, at.AddSeconds(30)));
        Assert.Equal("applied", accepted.Status);
        Assert.Null(accepted.Code);
        await using var verify = Database();
        Assert.Equal(DefectStatus.Cancelled, (await verify.DefectReports.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task Defect_list_pages_and_filters_on_the_server_with_photos_and_status()
    {
        var photo = await Photo();
        var photographed = await Report(severity: "major", photos: [photo]);
        var closed = await Report(severity: "minor");
        await PostOffice($"/api/v1/fleet/defects/{closed}/close", new { reason = "Inspected" });
        await Report(vehicle: await Truck(), severity: "minor");
        using var office = factory.AsDispatcher();
        var first = await Get(office, "/api/v1/fleet/defects?pageSize=1");
        var second = await Get(office, "/api/v1/fleet/defects?pageSize=1&page=2");
        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Single(first.GetProperty("items").EnumerateArray());
        Assert.NotEqual(first.GetProperty("items")[0].GetProperty("id").GetGuid(), second.GetProperty("items")[0].GetProperty("id").GetGuid());
        var filtered = await Get(office, $"/api/v1/fleet/defects?vehicleId={truckId}&severity=Major&statusGroup=open");
        Assert.Equal(1, filtered.GetProperty("total").GetInt32());
        var row = Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.Equal(photographed, row.GetProperty("id").GetGuid());
        Assert.Equal(1, row.GetProperty("photosCount").GetInt32());
        Assert.Equal("open", row.GetProperty("status").GetString());
        var completed = await Get(office, $"/api/v1/fleet/defects?vehicleId={truckId}&statusGroup=closed");
        Assert.Equal(closed, Assert.Single(completed.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var detail = await Get(office, $"/api/v1/fleet/defects/{photographed}");
        Assert.Equal(photo, Assert.Single(detail.GetProperty("photos").EnumerateArray()).GetProperty("id").GetGuid());
        await using var db = Database();
        Assert.Equal(3, await db.DefectReports.CountAsync(Cancellation));
        Assert.Equal(photographed, (await db.DefectPhotos.SingleAsync(Cancellation)).DefectId);
    }

    [Fact]
    public async Task Downgrade_requires_a_reason_is_audited_and_updates_readiness()
    {
        var id = await Report();
        using var office = factory.AsDispatcher();
        using var refused = await office.PostAsJsonAsync($"/api/v1/fleet/defects/{id}/downgrade", new { severity = "major", reason = " " }, Cancellation);
        var refusal = await Error(refused, HttpStatusCode.BadRequest, "validation_error");
        Assert.Equal("reason", Assert.Single(refusal.GetProperty("fields").EnumerateArray()).GetString());
        await Usable(false);
        await PostOffice($"/api/v1/fleet/defects/{id}/downgrade", new { severity = "major", reason = "Mechanic inspected brakes" });
        await Usable(true);
        await using var db = Database();
        Assert.Equal(DefectSeverity.Major, (await db.DefectReports.SingleAsync(Cancellation)).Severity);
        Assert.True(await db.AuditEntries.AnyAsync(row => row.EntityId == id && row.FieldName == "Severity" && row.Reason == "Mechanic inspected brakes", Cancellation));
    }

    [Fact]
    public async Task Close_without_repair_requires_a_reason_and_is_refused_when_a_repair_is_linked()
    {
        var standalone = await Report(severity: "minor");
        using (var office = factory.AsDispatcher())
        using (var refused = await office.PostAsJsonAsync($"/api/v1/fleet/defects/{standalone}/close", new { reason = " " }, Cancellation))
            await Error(refused, HttpStatusCode.BadRequest, "validation_error");
        await PostOffice($"/api/v1/fleet/defects/{standalone}/close", new { reason = "No repair needed after inspection" });
        var linked = await Report();
        var repair = await Repair(linked);
        using (var office = factory.AsDispatcher())
        using (var refused = await office.PostAsJsonAsync($"/api/v1/fleet/defects/{linked}/close", new { reason = "Valid reason" }, Cancellation))
            await Error(refused, HttpStatusCode.NotFound, "not_closable");
        await using var db = Database();
        Assert.Equal(DefectStatus.Closed, (await db.DefectReports.SingleAsync(row => row.Id == standalone, Cancellation)).Status);
        var saved = await db.DefectReports.SingleAsync(row => row.Id == linked, Cancellation);
        Assert.Equal(DefectStatus.InRepair, saved.Status);
        Assert.Equal(repair, saved.RepairId);
    }

    [Fact]
    public async Task Logged_report_saves_with_origin_office_from_chat()
    {
        var response = await PostOffice("/api/v1/fleet/defects", new { vehicleId = truckId, category = "lights", severity = "minor", description = "Driver called about lamp", reportedBy = reporterId });
        var id = response.GetProperty("id").GetGuid();
        await using var db = Database();
        var saved = await db.DefectReports.SingleAsync(Cancellation);
        Assert.Equal(id, saved.Id);
        Assert.Equal(DefectOrigin.OfficeFromChat, saved.Origin);
        Assert.Equal(reporterId, saved.ReportedBy);
        Assert.Equal("Driver called about lamp", saved.Description);
        Assert.Null(saved.CommandId);
        Assert.True(await db.AuditEntries.AnyAsync(row => row.EntityId == id && row.Reason == "office_from_chat", Cancellation));
    }

    [Fact]
    public async Task A08_opens_for_a_major_defect_open_over_three_days_and_resolves_on_repair()
    {
        var id = await Report(severity: "major");
        factory.Clock.Advance(TimeSpan.FromDays(3));
        await Sweep();
        await using (var before = Database())
            Assert.False(await before.Alerts.AnyAsync(row => row.RuleId == "A-08" && row.SubjectId == id && row.Status == AlertStatus.Open, Cancellation));
        factory.Clock.Advance(TimeSpan.FromSeconds(1));
        await Sweep();
        await using (var opened = Database())
            Assert.Equal(AlertStatus.Open, (await opened.Alerts.SingleAsync(row => row.RuleId == "A-08" && row.SubjectId == id, Cancellation)).Status);
        var repair = await Repair(id);
        await Done(repair);
        await using var db = Database();
        Assert.Equal(DefectStatus.Closed, (await db.DefectReports.SingleAsync(Cancellation)).Status);
        Assert.Equal(AlertStatus.Resolved, (await db.Alerts.SingleAsync(row => row.RuleId == "A-08" && row.SubjectId == id, Cancellation)).Status);
    }

    [Fact]
    public async Task Creating_a_repair_links_the_defects_and_moves_them_to_in_repair()
    {
        var first = await Report();
        var second = await Report(severity: "major");
        var repair = await Repair(first, second);
        await using var db = Database();
        var saved = await db.Maintenance.SingleAsync(Cancellation);
        Assert.Equal(repair, saved.Id);
        Assert.Equal(truckId, saved.VehicleId);
        Assert.Equal(new[] { first, second }, saved.DefectReportIds);
        Assert.Equal(MaintenanceStatus.Scheduled, saved.Status);
        Assert.All(await db.DefectReports.ToListAsync(Cancellation), defect =>
        {
            Assert.Equal(repair, defect.RepairId);
            Assert.Equal(DefectStatus.InRepair, defect.Status);
        });
    }

    [Fact]
    public async Task Repair_done_closes_its_defects_and_returns_the_truck()
    {
        var first = await Report();
        var second = await Report(severity: "major");
        var repair = await Repair(first, second);
        await Usable(false);
        await Done(repair);
        await Usable(true);
        await using var db = Database();
        Assert.Equal(MaintenanceStatus.Done, (await db.Maintenance.SingleAsync(Cancellation)).Status);
        Assert.All(await db.DefectReports.ToListAsync(Cancellation), defect =>
        {
            Assert.Equal(DefectStatus.Closed, defect.Status);
            Assert.Equal(repair, defect.RepairId);
            Assert.Equal(VehicleApiFactory.ActorId, defect.ClosedBy);
            Assert.NotNull(defect.ClosedAt);
        });
    }

    [Fact]
    public async Task Swap_options_list_usable_trucks_with_owner_and_in_use_since()
    {
        var owner = await Employee();
        await Cycle(owner, truckId);
        var started = factory.Clock.GetUtcNow().AddHours(-2);
        await using (var setup = Database())
        {
            setup.Shifts.Add(ShiftRules.Start(Guid.NewGuid(), owner, Today, started, truckId));
            await setup.SaveChangesAsync(Cancellation);
        }
        var blocked = await Truck();
        await Report(vehicle: blocked);
        using var office = factory.AsDispatcher();
        var body = await Get(office, $"/api/v1/fleet/swap-options?employeeId={reporterId}&date={Today:yyyy-MM-dd}");
        var option = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(truckId, option.GetProperty("vehicleId").GetGuid());
        Assert.Equal("Reporter Driver", option.GetProperty("belongsTo").GetString());
        Assert.Equal("Reporter Driver", option.GetProperty("runningShiftUserName").GetString());
        Assert.Equal(started, option.GetProperty("runningShiftStartAt").GetDateTimeOffset());
        await using var db = Database();
        Assert.Equal(owner, (await db.Shifts.SingleAsync(Cancellation)).EmployeeId);
    }

    [Fact]
    public async Task Swap_options_say_when_no_truck_is_usable()
    {
        await Report();
        using var office = factory.AsDispatcher();
        var body = await Get(office, $"/api/v1/fleet/swap-options?employeeId={reporterId}&date={Today:yyyy-MM-dd}");
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        await using var db = Database();
        Assert.Equal(DefectSeverity.Critical, (await db.DefectReports.SingleAsync(Cancellation)).Severity);
        Assert.False(await db.Assignments.AnyAsync(Cancellation));
    }

    [Fact]
    public async Task Swap_creates_an_assignment_that_wins_over_the_cycle_and_shows_on_the_schedule()
    {
        await Cycle(reporterId, truckId);
        var replacement = await Truck();
        var assignment = await Swap(replacement);
        using (var driver = factory.AsDriver(reporterId))
        {
            var snapshot = await Get(driver, "/api/v1/driver/shifts");
            Assert.Equal(replacement, snapshot.GetProperty("suggestedTruckId").GetGuid());
        }
        using var office = factory.AsDispatcher();
        var schedule = await Get(office, "/api/v1/schedule?from=2026-11-06&to=2026-11-07");
        Assert.Contains(schedule.GetProperty("swaps").EnumerateArray(), row => row.GetProperty("id").GetGuid() == assignment && row.GetProperty("vehicleId").GetGuid() == replacement);
        await using var db = Database();
        Assert.Equal(replacement, (await db.Assignments.SingleAsync(Cancellation)).VehicleId);
        Assert.Equal(truckId, (await db.Cycles.SingleAsync(Cancellation)).VehicleId);
    }

    [Fact]
    public async Task Swap_changes_tell_the_driver_and_the_displaced_driver()
    {
        var displaced = await Employee();
        await Cycle(displaced, truckId);
        var id = await Swap(truckId);
        await AssertAssignmentNotifications(reporterId, displaced, 1);
        using var office = factory.AsDispatcher();
        using var response = await office.PutAsJsonAsync($"/api/v1/fleet/assignments/{id}", new { from = Today, to = Today.AddDays(2) }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertAssignmentNotifications(reporterId, displaced, 2);
        await using var db = Database();
        Assert.Equal(Today.AddDays(2), (await db.Assignments.SingleAsync(Cancellation)).To);
    }

    [Fact]
    public async Task Repair_done_ends_the_linked_swap_and_tells_both_drivers()
    {
        var defect = await Report();
        var repair = await Repair(defect);
        var replacement = await Truck();
        var displaced = await Employee();
        await Cycle(displaced, replacement);
        var assignment = await Swap(replacement, repair);
        await AssertAssignmentNotifications(reporterId, displaced, 1);
        await Done(repair);
        await AssertAssignmentNotifications(reporterId, displaced, 2);
        await using var db = Database();
        var saved = await db.Assignments.SingleAsync(Cancellation);
        Assert.Equal(assignment, saved.Id);
        Assert.Equal(repair, saved.MaintenanceId);
        Assert.Equal(Today, saved.To);
        Assert.Equal(MaintenanceStatus.Done, (await db.Maintenance.SingleAsync(Cancellation)).Status);
    }

    [Fact]
    public async Task HR_cannot_use_defects_repairs_or_swaps_and_drivers_cannot_use_office_endpoints()
    {
        var defect = await Report();
        var repair = await Repair(defect);
        var replacement = await Truck();
        var assignment = await Swap(replacement);
        foreach (var driverRole in new[] { false, true })
        {
            using var client = driverRole ? factory.AsDriver(reporterId) : factory.AsHr();
            foreach (var path in new[] { "/api/v1/fleet/defects", $"/api/v1/fleet/defects/{defect}", $"/api/v1/fleet/vehicles/{truckId}/defects", "/api/v1/fleet/maintenance", $"/api/v1/fleet/vehicles/{truckId}/maintenance", $"/api/v1/fleet/swap-options?employeeId={reporterId}&date=2026-11-06" })
            {
                using var response = await client.GetAsync(path, Cancellation);
                await Error(response, HttpStatusCode.Forbidden, "permission_denied");
            }
            var writes = new (string Path, object Body)[]
            {
                ("/api/v1/fleet/defects", new { vehicleId = truckId, category = "brakes", severity = "minor", description = "Valid", reportedBy = reporterId }),
                ($"/api/v1/fleet/defects/{defect}/confirm", new { }),
                ($"/api/v1/fleet/defects/{defect}/downgrade", new { severity = "major", reason = "Valid reason" }),
                ($"/api/v1/fleet/defects/{defect}/close", new { reason = "Valid reason" }),
                ("/api/v1/fleet/defects/upload-links", new { contentType = "image/jpeg", sizeBytes = 12 }),
                ("/api/v1/fleet/maintenance", new { vehicleId = truckId, workshop = "Workshop", scheduledFor = Today, defectReportIds = new[] { defect } }),
                ($"/api/v1/fleet/maintenance/{repair}/start", new { }),
                ($"/api/v1/fleet/maintenance/{repair}/done", new { doneAt = Today }),
                ($"/api/v1/fleet/maintenance/{repair}/cancel", new { reason = "Valid reason" }),
                ("/api/v1/fleet/assignments", new { employeeId = reporterId, vehicleId = replacement }),
                ($"/api/v1/fleet/assignments/{assignment}/end", new { })
            };
            foreach (var write in writes)
            {
                using var response = await client.PostAsJsonAsync(write.Path, write.Body, Cancellation);
                await Error(response, HttpStatusCode.Forbidden, "permission_denied");
            }
            using var edited = await client.PutAsJsonAsync($"/api/v1/fleet/assignments/{assignment}", new { from = Today, to = Today.AddDays(1) }, Cancellation);
            await Error(edited, HttpStatusCode.Forbidden, "permission_denied");
        }
        await using var db = Database();
        Assert.Equal(DefectStatus.InRepair, (await db.DefectReports.SingleAsync(Cancellation)).Status);
        Assert.Equal(MaintenanceStatus.Scheduled, (await db.Maintenance.SingleAsync(Cancellation)).Status);
        Assert.Null((await db.Assignments.SingleAsync(Cancellation)).To);
        Assert.False(await db.PendingFiles.AnyAsync(Cancellation));
    }

    private async Task<Guid> Employee()
    {
        var id = Guid.NewGuid();
        await using var db = Database();
        db.Employees.Add(new Employee { Id = id, FirstName = "Reporter", LastName = "Driver", Email = $"{id:N}@example.invalid", Roles = "[\"driver\"]", StartDate = new(2026, 1, 1) });
        await db.SaveChangesAsync(Cancellation);
        return id;
    }

    private async Task<Guid> Truck()
    {
        using var office = factory.AsDispatcher();
        var truck = await factory.CreateTruck(office);
        await ReadinessTestSupport.AddAllVehicleDocuments(factory, truck.Id);
        return truck.Id;
    }

    private async Task Cycle(Guid employee, Guid vehicle)
    {
        await using var db = Database();
        db.Cycles.Add(new Cycle { EmployeeId = employee, VehicleId = vehicle, From = Today.AddDays(-2), To = Today.AddDays(10), Status = CycleStatus.Active, Source = CycleSource.Manual });
        await db.SaveChangesAsync(Cancellation);
    }

    private DriverCommand Command(string type, Guid id, object payload, DateTimeOffset? at = null) =>
        new(Guid.NewGuid(), type, 1, id, 0, at ?? factory.Clock.GetUtcNow(), JsonSerializer.SerializeToElement(payload, VehicleApiFactory.JsonOptions));

    private async Task<DriverCommandResult> Apply(DriverCommand command, Guid? employee = null)
    {
        using var client = factory.AsDriver(employee ?? reporterId);
        using var response = await client.PostAsJsonAsync("/api/v1/driver/commands/batch", new DriverCommandBatch([command]), VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var batch = await response.Content.ReadFromJsonAsync<DriverCommandBatchResult>(VehicleApiFactory.JsonOptions, Cancellation);
        return Assert.Single(batch!.Results);
    }

    private async Task<Guid> Report(Guid? vehicle = null, string severity = "critical", DateTimeOffset? at = null, Guid[]? photos = null)
    {
        var id = Guid.NewGuid();
        var result = await Apply(Command("defect.report", id, new { vehicleId = vehicle ?? truckId, category = "brakes", severity, description = "Brake noise", photos = photos ?? [] }, at));
        Assert.Equal("applied", result.Status);
        Assert.Null(result.Code);
        return id;
    }

    private async Task<Guid> Photo()
    {
        using var driver = factory.AsDriver(reporterId);
        using var response = await driver.PostAsJsonAsync("/api/v1/driver/defects/upload-links", new { contentType = "image/jpeg", sizeBytes = 12 }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        factory.ObjectStore.Put(factory.ObjectStore.Linked.Last(), 12, "image/jpeg");
        return body.GetProperty("fileId").GetGuid();
    }

    private async Task<JsonElement> PostOffice(string path, object body)
    {
        using var office = factory.AsDispatcher();
        using var response = await office.PostAsJsonAsync(path, body, VehicleApiFactory.JsonOptions, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
    }

    private async Task<Guid> Repair(params Guid[] defects) =>
        (await PostOffice("/api/v1/fleet/maintenance", new { vehicleId = truckId, workshop = "Brake workshop", scheduledFor = Today.AddDays(7), defectReportIds = defects })).GetProperty("id").GetGuid();

    private async Task Done(Guid repair) =>
        await PostOffice($"/api/v1/fleet/maintenance/{repair}/done", new { odometerAtDoneKm = 1200 });

    private async Task<Guid> Swap(Guid vehicle, Guid? repair = null) =>
        (await PostOffice("/api/v1/fleet/assignments", new { employeeId = reporterId, vehicleId = vehicle, maintenanceId = repair })).GetProperty("id").GetGuid();

    private async Task Usable(bool expected)
    {
        using var office = factory.AsDispatcher();
        var body = await Get(office, $"/api/v1/fleet/vehicles/{truckId}");
        Assert.Equal(expected, body.GetProperty("usable").GetBoolean());
        Assert.Equal(expected ? "active" : "off_road", body.GetProperty("shownStatus").GetString());
    }

    private async Task AssertAssignmentNotifications(Guid assigned, Guid displaced, int count)
    {
        await using var db = Database();
        var notifications = await db.Notifications.Where(row => row.TemplateCode == "assignment_changed").ToListAsync(Cancellation);
        Assert.Equal(count * 2, notifications.Count);
        Assert.Equal(count, notifications.Count(row => row.EmployeeId == assigned));
        Assert.Equal(count, notifications.Count(row => row.EmployeeId == displaced));
    }

    private async Task Sweep()
    {
        factory.Services.GetRequiredService<IConfiguration>()["Jobs:Secret"] = "defect-test-only";
        using var client = factory.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "defect-test-only");
        var body = await Get(client, "/jobs/alerts-sweep");
        Assert.Equal("succeeded", body.GetProperty("status").GetString());
    }

    private async Task<JsonElement> Get(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
    }

    private async Task<JsonElement> Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "defect_test_setup" };
    }
}

