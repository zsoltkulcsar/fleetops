using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Migrations;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public sealed class CycleClashTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private static readonly DateOnly Today = new(2026, 11, 6);
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Overlap_opens_one_A21_per_cycle_and_becomes_critical_on_start_day()
    {
        var vehicleId = await UsableTruck();
        var first = await AddCycle(vehicleId, Today.AddDays(4), Today.AddDays(17));
        var second = await AddCycle(vehicleId, Today.AddDays(10), Today.AddDays(23));
        await Sweep();
        await Sweep();
        var alert = await AlertTestSupport.Alert(factory, "A-21", "cycle", first.Id);
        Assert.Equal("Open", alert.Status);
        Assert.Equal("Warning", alert.Severity);
        Assert.Equal("dispatcher", alert.Role);
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-21", first.Id));
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-21", second.Id));
        factory.Clock.Advance(TimeSpan.FromDays(4));
        await Sweep();
        Assert.Equal("Critical", (await AlertTestSupport.Alert(factory, "A-21", "cycle", first.Id)).Severity);
    }

    [Fact]
    public async Task Shared_handover_date_does_not_raise_a_clash()
    {
        var vehicleId = await UsableTruck();
        var first = await AddCycle(vehicleId, Today, Today.AddDays(14));
        var second = await AddCycle(vehicleId, Today.AddDays(14), Today.AddDays(28));
        await Sweep();
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-21", first.Id));
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-21", second.Id));
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(6, false)]
    public async Task Readiness_is_checked_on_each_of_the_first_seven_dates_only(int expiryOffset, bool expected)
    {
        var start = Today.AddDays(3);
        var vehicleId = await UsableTruck(start.AddDays(expiryOffset));
        var cycle = await AddCycle(vehicleId, start, start.AddDays(13));
        await Sweep();
        Assert.Equal(expected ? 1 : 0, await AlertTestSupport.Count(factory, "A-21", cycle.Id));
    }

    [Fact]
    public async Task Vehicle_command_fans_out_and_clears_cycle_alert_when_repaired()
    {
        var vehicleId = await UsableTruck();
        var cycle = await AddCycle(vehicleId, Today.AddDays(1), Today.AddDays(14));
        using var dispatcher = factory.AsDispatcher();
        using var blocked = await dispatcher.PostAsJsonAsync($"/api/v1/fleet/vehicles/{vehicleId}/off-road", new { version = 1, manualOffRoad = true, reason = "Test workshop" }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-21", cycle.Id));
        using var repaired = await dispatcher.PostAsJsonAsync($"/api/v1/fleet/vehicles/{vehicleId}/off-road", new { version = 2, manualOffRoad = false, reason = "Test repaired" }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, repaired.StatusCode);
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-21", cycle.Id));
        Assert.Equal("Resolved", (await AlertTestSupport.Alert(factory, "A-21", "cycle", cycle.Id)).Status);
    }

    [Fact]
    public async Task Dispatcher_sees_cycle_alert_with_subject_name_in_attention()
    {
        var vehicleId = await UsableTruck();
        var first = await AddCycle(vehicleId, Today, Today.AddDays(13));
        await AddCycle(vehicleId, Today.AddDays(2), Today.AddDays(15));
        await Sweep();
        using var dispatcher = factory.AsDispatcher();
        using var response = await dispatcher.GetAsync("/api/v1/attention", Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        var group = Assert.Single(body.GetProperty("groups").EnumerateArray(), item => item.GetProperty("subjectId").GetGuid() == first.Id);
        Assert.Equal("cycle", group.GetProperty("subjectType").GetString());
        Assert.Contains("Alert Test", group.GetProperty("subjectName").GetString());
        Assert.Contains(group.GetProperty("items").EnumerateArray(), item => item.GetProperty("ruleId").GetString() == "A-21");
        Assert.Equal(1, await AlertTestSupport.Count(factory, "A-21", first.Id));
    }

    [Fact]
    public async Task Moving_a_cycle_clears_the_alert_on_both_it_and_its_previous_peer()
    {
        var vehicleId = await UsableTruck();
        var first = await AddCycle(vehicleId, Today, Today.AddDays(13));
        var second = await AddCycle(vehicleId, Today.AddDays(2), Today.AddDays(15));
        await Sweep();
        using var dispatcher = factory.AsDispatcher();
        using var response = await dispatcher.PutAsJsonAsync($"/api/v1/schedule/cycles/{second.Id}", new
        {
            version = second.Version, employeeId = second.EmployeeId, vehicleId,
            from = Today.AddDays(13), to = Today.AddDays(26), reason = "Test handover rescheduled",
        }, Cancellation);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-21", first.Id));
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-21", second.Id));
        await using var db = Database();
        var moved = await db.Cycles.SingleAsync(cycle => cycle.Id == second.Id, Cancellation);
        Assert.Equal(Today.AddDays(13), moved.From);
        Assert.Equal(2, moved.Version);
    }

    [Fact]
    public async Task Cancelled_cycle_clears_its_alert_and_the_peer_on_next_sweep()
    {
        var vehicleId = await UsableTruck();
        var first = await AddCycle(vehicleId, Today, Today.AddDays(13));
        var second = await AddCycle(vehicleId, Today.AddDays(2), Today.AddDays(15));
        await Sweep();
        await using (var db = Database())
        {
            var cancelled = await db.Cycles.SingleAsync(cycle => cycle.Id == second.Id, Cancellation);
            cancelled.Status = CycleStatus.Cancelled;
            cancelled.Version++;
            await db.SaveChangesAsync(Cancellation);
        }
        await Sweep();
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-21", first.Id));
        Assert.Equal(0, await AlertTestSupport.Count(factory, "A-21", second.Id));
        Assert.Equal("Resolved", (await AlertTestSupport.Alert(factory, "A-21", "cycle", second.Id)).Status);
    }

    private async Task<Guid> UsableTruck(DateOnly? huExpiry = null)
    {
        using var dispatcher = factory.AsDispatcher();
        var truck = await factory.CreateTruck(dispatcher);
        foreach (var type in new[] { DocumentType.Registration, DocumentType.HuReport, DocumentType.SpReport, DocumentType.TachoCalibration, DocumentType.Insurance, DocumentType.CommunityLicence })
            await ReadinessTestSupport.AddDocument(factory, DocumentOwnerType.Vehicle, truck.Id, type, type == DocumentType.HuReport ? huExpiry ?? Today.AddYears(1) : Today.AddYears(1));
        return truck.Id;
    }

    private async Task<Cycle> AddCycle(Guid vehicleId, DateOnly from, DateOnly to)
    {
        var employeeId = await AlertTestSupport.AddEmployee(factory);
        var cycle = new Cycle { EmployeeId = employeeId, VehicleId = vehicleId, From = from, To = to, Source = CycleSource.Manual };
        await using var db = Database();
        db.Cycles.Add(cycle);
        await db.SaveChangesAsync(Cancellation);
        return cycle;
    }

    private async Task Sweep()
    {
        factory.Services.GetRequiredService<IConfiguration>()["Jobs:Secret"] = "cycle-clash-test-only";
        using var client = factory.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cycle-clash-test-only");
        using var response = await client.GetAsync("/jobs/alerts-sweep", Cancellation);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Cancellation));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("succeeded", body.GetProperty("status").GetString());
    }

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "cycle_clash_test_setup" };
    }
}
