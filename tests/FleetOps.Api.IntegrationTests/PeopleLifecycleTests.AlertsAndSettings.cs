using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FleetOps.Tests;

public sealed partial class PeopleLifecycleTests
{
    private static JsonElement[] AttentionItems(JsonElement body) => body.GetProperty("groups").EnumerateArray()
        .SelectMany(group => group.GetProperty("items").EnumerateArray()).ToArray();

    private async Task Settings(HttpClient owner, string key, object value)
    {
        var body = await Body(await owner.PutAsJsonAsync("/api/v1/settings", new Dictionary<string, object> { [key] = value }));
        if (value is int number)
        {
            Assert.Equal(number, body.GetProperty("values").GetProperty(key).GetInt32());
        }
    }

    [Fact]
    public async Task A01_opens_within_lead_days_from_settings_and_tells_the_driver()
    {
        var id = await Employee();
        await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, id, DocumentType.DriverCard, Today.AddDays(13));
        using var owner = factory.AsOwner();
        await Settings(owner, "documents.lead_days.driver_card", 12);
        await AlertTestSupport.Sweep(factory);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-01", id, scope: "driver_card"));
        factory.Clock.Advance(TimeSpan.FromDays(1));
        await AlertTestSupport.Sweep(factory);
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-01", id, scope: "driver_card"));
        using var hr = factory.AsHr();
        var attention = await Body(await hr.GetAsync("/api/v1/attention"));
        Assert.Contains(AttentionItems(attention), item => item.GetProperty("ruleId").GetString() == "A-01" && item.GetProperty("scopeKey").GetString() == "driver_card");
        using var driver = factory.AsDriver(id);
        var notifications = await Body(await driver.GetAsync("/api/v1/notifications"));
        Assert.Contains(notifications.GetProperty("items").EnumerateArray(), item => item.GetProperty("templateCode").GetString() == "doc_expiring");
        await using var db = Database();
        Assert.Equal(12, (await db.Settings.SingleAsync(setting => setting.Key == "documents.lead_days.driver_card")).Value);
        Assert.Single(await db.Notifications.Where(notification => notification.EmployeeId == id && notification.TemplateCode == "doc_expiring").ToArrayAsync());
    }

    [Fact]
    public async Task A01_is_hidden_from_dispatchers_until_seven_days_and_masks_medical()
    {
        var id = await Employee();
        await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, id, DocumentType.Medical, Today.AddDays(8));
        await AlertTestSupport.Sweep(factory);
        using var dispatcher = factory.AsDispatcher();
        var hidden = await Body(await dispatcher.GetAsync("/api/v1/attention"));
        Assert.DoesNotContain(AttentionItems(hidden), item => item.GetProperty("ruleId").GetString() == "A-01");
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-01", id, scope: "medical"));
        factory.Clock.Advance(TimeSpan.FromDays(1));
        await AlertTestSupport.Sweep(factory);
        var visible = await Body(await dispatcher.GetAsync("/api/v1/attention"));
        var alert = Assert.Single(AttentionItems(visible), item => item.GetProperty("ruleId").GetString() == "A-01");
        Assert.Equal("Document", alert.GetProperty("scopeKey").GetString());
        Assert.True(alert.GetProperty("readOnly").GetBoolean());
        Assert.Empty(alert.GetProperty("actions").EnumerateArray());
        Assert.DoesNotContain("medical", visible.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var stored = await AlertTestSupport.Alert(factory, "A-01", "employee", id, "medical");
        Assert.Equal("Open", stored.Status);
        Assert.Equal("medical", stored.Scope);
    }

    [Fact]
    public async Task A04_opens_for_a_vehicle_document_and_for_a_lease_end()
    {
        using var dispatcher = factory.AsDispatcher();
        var truck = await factory.CreateTruck(dispatcher);
        await ReadinessTestSupport.AddAllVehicleDocuments(factory, truck.Id);
        await using (var db = Database())
        {
            var vehicle = await db.Vehicles.SingleAsync(vehicle => vehicle.Id == truck.Id);
            vehicle.Ownership = VehicleOwnership.Leased;
            vehicle.LeaseEndDate = Today.AddDays(10);
            (await db.Documents.SingleAsync(document => document.OwnerId == truck.Id && document.Type == DocumentType.HuReport)).ExpiresAt = Today.AddDays(10);
            await db.SaveChangesAsync();
        }
        await AlertTestSupport.Sweep(factory);
        var body = await Body(await dispatcher.GetAsync("/api/v1/attention"));
        var alerts = AttentionItems(body).Where(item => item.GetProperty("ruleId").GetString() == "A-04").ToArray();
        Assert.Contains(alerts, item => item.GetProperty("scopeKey").GetString() == "hu_report");
        Assert.Contains(alerts, item => item.GetProperty("scopeKey").GetString() == "lease_end");
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-04", truck.Id, scope: "hu_report"));
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-04", truck.Id, scope: "lease_end"));
    }

    [Fact]
    public async Task Settings_change_is_read_by_the_next_evaluation()
    {
        var id = await Employee();
        await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, id, DocumentType.Code95, Today.AddDays(20));
        using var owner = factory.AsOwner();
        await Settings(owner, "documents.lead_days.code95", 19);
        await AlertTestSupport.Sweep(factory);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-01", id, scope: "code95"));
        await Settings(owner, "documents.lead_days.code95", 20);
        await AlertTestSupport.Sweep(factory);
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-01", id, scope: "code95"));
        var attention = await Body(await owner.GetAsync("/api/v1/attention"));
        Assert.Contains(AttentionItems(attention), item => item.GetProperty("ruleId").GetString() == "A-01" && item.GetProperty("scopeKey").GetString() == "code95");
        await using var db = Database();
        Assert.Equal(20, (await db.Settings.SingleAsync(setting => setting.Key == "documents.lead_days.code95")).Value);
    }

    [Fact]
    public async Task Hr_may_change_only_payroll_settings_and_holidays()
    {
        using var hr = factory.AsHr();
        await Settings(hr, "company.payroll_cutoff_day", 12);
        await Body(await hr.PostAsJsonAsync("/api/v1/settings/holidays", new { date = Today, name = "Company holiday" }));
        await using (var db = Database())
        {
            Assert.Equal(12, (await db.Settings.SingleAsync(setting => setting.Key == "company.payroll_cutoff_day")).Value);
            Assert.Equal("Company holiday", (await db.CompanyHolidays.SingleAsync(holiday => holiday.Date == Today)).Name);
        }
        foreach (var key in new[] { "company.max_shift_hours", "vehicles.lease_lead_days", "alerts.a08.days", "documents.lead_days.medical" })
        {
            await Body(await hr.PutAsJsonAsync("/api/v1/settings", new Dictionary<string, int> { [key] = 10 }), HttpStatusCode.Forbidden, "permission_denied");
            await using var db = Database();
            Assert.False(await db.Settings.AnyAsync(setting => setting.Key == key));
        }
        await Body(await hr.DeleteAsync("/api/v1/settings/holidays/2026-11-06"));
        await using var verify = Database();
        Assert.False(await verify.CompanyHolidays.AnyAsync(holiday => holiday.Date == Today));
    }

    [Fact]
    public async Task Releasable_types_refuse_insurance_and_registration()
    {
        using var owner = factory.AsOwner();
        foreach (var type in new[] { "insurance", "registration" })
        {
            var body = await Body(await owner.PutAsJsonAsync("/api/v1/settings", new Dictionary<string, object> { ["company.releasable_doc_types"] = new[] { type } }), HttpStatusCode.BadRequest, "validation_error");
            Assert.Contains("company.releasable_doc_types", body.GetProperty("fields").EnumerateArray().Select(field => field.GetString()));
            await using var db = Database();
            Assert.False(await db.SettingLists.AnyAsync());
        }
    }

    private async Task<Guid> ExpiredTruck(DocumentType type)
    {
        using var dispatcher = factory.AsDispatcher();
        var truck = await factory.CreateTruck(dispatcher);
        await ReadinessTestSupport.AddAllVehicleDocuments(factory, truck.Id);
        await using var db = Database();
        (await db.Documents.SingleAsync(document => document.OwnerId == truck.Id && document.Type == type)).ExpiresAt = Today.AddDays(-1);
        await db.SaveChangesAsync();
        return truck.Id;
    }

    [Fact]
    public async Task Release_is_refused_when_not_enabled_or_not_allowed()
    {
        var id = await ExpiredTruck(DocumentType.Insurance);
        using var dispatcher = factory.AsDispatcher();
        await Body(await dispatcher.PostAsJsonAsync($"/api/v1/fleet/vehicles/{id}/release", new { reason = "Temporary release" }), HttpStatusCode.Conflict, "release_not_enabled");
        await using (var db = Database())
        {
            Assert.Empty(await db.VehicleOverrides.ToArrayAsync());
        }
        using var owner = factory.AsOwner();
        await Settings(owner, "company.releasable_doc_types", new[] { "hu_report" });
        using var dispatcherAgain = factory.AsDispatcher();
        var refusal = await Body(await dispatcherAgain.PostAsJsonAsync($"/api/v1/fleet/vehicles/{id}/release", new { reason = "Temporary release" }), HttpStatusCode.Conflict, "release_not_allowed");
        Assert.Contains("insurance", refusal.GetProperty("types").EnumerateArray().Select(type => type.GetString()));
        await using var verify = Database();
        Assert.Empty(await verify.VehicleOverrides.ToArrayAsync());
        Assert.Equal(Today.AddDays(-1), (await verify.Documents.SingleAsync(document => document.OwnerId == id && document.Type == DocumentType.Insurance)).ExpiresAt);
    }

    [Fact]
    public async Task Release_makes_the_truck_selectable_until_valid_until_and_opens_a13()
    {
        await ReadinessTestSupport.AddEmployee(factory, VehicleApiFactory.ActorId, "[\"dispatcher\"]");
        var driverId = await Employee();
        await Documents(driverId);
        var truckId = await ExpiredTruck(DocumentType.HuReport);
        await AlertTestSupport.Sweep(factory);
        using var owner = factory.AsOwner();
        await Settings(owner, "company.releasable_doc_types", new[] { "hu_report" });
        using var beforeDriver = factory.AsDriver(driverId);
        var before = await Body(await beforeDriver.GetAsync("/api/v1/driver/shifts"));
        Assert.False(Assert.Single(before.GetProperty("trucks").EnumerateArray(), truck => truck.GetProperty("id").GetGuid() == truckId).GetProperty("usable").GetBoolean());
        using var dispatcher = factory.AsDispatcher();
        var released = await Body(await dispatcher.PostAsJsonAsync($"/api/v1/fleet/vehicles/{truckId}/release", new { reason = "Approved temporary exception" }));
        var overrideId = released.GetProperty("overrideId").GetGuid();
        var validUntil = released.GetProperty("validUntil").GetDateTimeOffset();
        Assert.Equal(new DateOnly(2026, 11, 7), DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(validUntil, "Europe/Berlin").DateTime));
        using var driver = factory.AsDriver(driverId);
        var snapshot = await Body(await driver.GetAsync("/api/v1/driver/shifts"));
        var choice = Assert.Single(snapshot.GetProperty("trucks").EnumerateArray(), truck => truck.GetProperty("id").GetGuid() == truckId);
        Assert.True(choice.GetProperty("usable").GetBoolean());
        Assert.Equal(overrideId, choice.GetProperty("release").GetProperty("overrideId").GetGuid());
        var shiftId = Guid.NewGuid();
        var batch = await Body(await driver.PostAsJsonAsync("/api/v1/driver/commands/batch", new
        {
            commands = new[] { new { commandId = Guid.NewGuid(), type = "shift.start", schemaVersion = 1, aggregateId = shiftId, baseVersion = 0, deviceTime = factory.Clock.GetUtcNow(), payload = new { truckId, departureCheckOk = true, odometerKm = 1000 } } }
        }));
        var result = Assert.Single(batch.GetProperty("results").EnumerateArray());
        Assert.Equal("applied", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("code").ValueKind);
        await using (var db = Database())
        {
            var stored = await db.VehicleOverrides.SingleAsync();
            Assert.Equal(overrideId, stored.Id);
            Assert.Equal(truckId, stored.VehicleId);
            Assert.Equal("Approved temporary exception", stored.Reason);
            Assert.Equal(validUntil.UtcTicks / 10, stored.ValidUntil.UtcTicks / 10);
            Assert.Equal(overrideId, (await db.Shifts.SingleAsync(shift => shift.Id == shiftId)).OverrideId);
            var review = Assert.Single(await db.Alerts.Where(alert => alert.RuleId == "A-13").ToArrayAsync());
            Assert.Equal(AlertStatus.Open, review.Status);
            Assert.Equal("hr,owner", review.AssignedRole);
        }
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-05", truckId, scope: "hu_report"));
        factory.Clock.SetUtcNow(validUntil.AddSeconds(1));
        var expired = await Body(await driver.GetAsync("/api/v1/driver/shifts"));
        Assert.False(Assert.Single(expired.GetProperty("trucks").EnumerateArray(), truck => truck.GetProperty("id").GetGuid() == truckId).GetProperty("usable").GetBoolean());
        await using var verify = Database();
        Assert.Equal(ShiftStatus.Running, (await verify.Shifts.SingleAsync(shift => shift.Id == shiftId)).Status);
    }
}

