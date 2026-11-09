using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Migrations;
using FleetOps.Modules.Schedule;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public sealed class CycleRolloverTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private static readonly DateOnly Today = new(2026, 11, 6);
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    [Fact]
    public async Task Saving_a_pattern_generates_twelve_weeks_without_notifications()
    {
        using var client = factory.AsDispatcher();
        var (employeeId, pattern) = await Seed(client);
        var before = await NotificationCount();
        using var response = await Save(client, employeeId, 0, pattern);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CyclePatternResult>(Cancellation);
        Assert.Equal(1, result!.Version);
        Assert.Equal(3, result.Generated);
        var cycles = await ReadCycles(employeeId);
        Assert.Equal(new int?[] { 0, 1, 2 }, cycles.Select(cycle => cycle.PatternSlot));
        Assert.Equal(new[] { Today, Today.AddDays(28), Today.AddDays(56) }, cycles.Select(cycle => cycle.From));
        Assert.All(cycles, cycle => Assert.Equal(CycleStatus.Planned, cycle.Status));
        Assert.Equal(before, await NotificationCount());
        await using var db = Database();
        var employee = await db.Employees.SingleAsync(employee => employee.Id == employeeId, Cancellation);
        Assert.Equal(pattern, employee.CyclePattern);
        Assert.Equal(1, employee.CyclePatternVersion);
        Assert.True(await db.AuditEntries.AnyAsync(entry => entry.EntityId == employeeId && entry.FieldName == "CyclePattern", Cancellation));
    }

    [Fact]
    public async Task Repeated_job_preserves_manual_edit_and_adds_only_the_new_horizon_slot()
    {
        using var client = factory.AsDispatcher();
        var (employeeId, pattern) = await Seed(client);
        using var saved = await Save(client, employeeId, 0, pattern);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var initial = await ReadCycles(employeeId);
        var manualId = initial[1].Id;
        await using (var db = Database())
        {
            var manual = await db.Cycles.SingleAsync(cycle => cycle.Id == manualId, Cancellation);
            manual.Source = CycleSource.Manual;
            manual.From = manual.From.AddDays(3);
            manual.Version = 2;
            await db.SaveChangesAsync(Cancellation);
        }
        factory.Clock.Advance(TimeSpan.FromDays(7));
        var before = await NotificationCount();
        using var first = await RunJob();
        using var second = await RunJob();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var cycles = await ReadCycles(employeeId);
        Assert.Equal(4, cycles.Count);
        Assert.Equal(4, cycles.Select(cycle => cycle.PatternSlot).Distinct().Count());
        var manualAfter = Assert.Single(cycles, cycle => cycle.Id == manualId);
        Assert.Equal(Today.AddDays(31), manualAfter.From);
        Assert.Equal(2, manualAfter.Version);
        Assert.Equal(CycleSource.Manual, manualAfter.Source);
        Assert.Equal(before, await NotificationCount());
    }

    [Fact]
    public async Task Changed_pattern_regenerates_future_generated_cycles_and_keeps_manual_and_current()
    {
        using var client = factory.AsDispatcher();
        var (employeeId, pattern) = await Seed(client);
        using var first = await Save(client, employeeId, 0, pattern);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var initial = await ReadCycles(employeeId);
        await using (var db = Database())
        {
            var manual = await db.Cycles.SingleAsync(cycle => cycle.Id == initial[1].Id, Cancellation);
            manual.Source = CycleSource.Manual;
            manual.From = Today.AddDays(30);
            manual.Version = 2;
            await db.SaveChangesAsync(Cancellation);
        }
        using var second = await Save(client, employeeId, 1, pattern with { OnWeeks = 1, OffWeeks = 2 });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var cycles = await ReadCycles(employeeId);
        Assert.Equal(4, cycles.Count);
        Assert.Contains(cycles, cycle => cycle.Id == initial[0].Id && cycle.To == initial[0].To);
        Assert.Contains(cycles, cycle => cycle.Id == initial[1].Id && cycle.From == Today.AddDays(30) && cycle.Source == CycleSource.Manual);
        Assert.DoesNotContain(cycles, cycle => cycle.Id == initial[2].Id);
        Assert.Contains(cycles, cycle => cycle.PatternSlot == 2 && cycle.From == Today.AddDays(42) && cycle.To == Today.AddDays(48));
        Assert.Contains(cycles, cycle => cycle.PatternSlot == 3 && cycle.From == Today.AddDays(63));
    }

    [Fact]
    public async Task Concurrent_pattern_saves_accept_one_version_and_roll_back_the_loser()
    {
        using var client = factory.AsDispatcher();
        var (employeeId, pattern) = await Seed(client);
        var responses = await Task.WhenAll(Save(client, employeeId, 0, pattern), Save(client, employeeId, 0, pattern with { OnWeeks = 1, OffWeeks = 1 }));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var refused = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            var problem = await refused.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
            Assert.Equal("version_conflict", problem.GetProperty("code").GetString());
            await using var db = Database();
            var employee = await db.Employees.SingleAsync(employee => employee.Id == employeeId, Cancellation);
            Assert.Equal(1, employee.CyclePatternVersion);
            var cycles = await ReadCycles(employeeId);
            Assert.Equal(employee.CyclePattern!.OnWeeks == 1 ? 6 : 3, cycles.Count);
            Assert.Equal(cycles.Count, cycles.Select(cycle => cycle.PatternSlot).Distinct().Count());
            Assert.Equal(1, await db.AuditEntries.CountAsync(entry => entry.EntityId == employeeId && entry.FieldName == "CyclePattern", Cancellation));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Theory]
    [InlineData("hr")]
    [InlineData("driver")]
    public async Task Non_dispatching_roles_cannot_save_a_pattern(string role)
    {
        using var dispatcher = factory.AsDispatcher();
        var (employeeId, pattern) = await Seed(dispatcher);
        using var client = role == "hr" ? factory.AsHr() : factory.AsDriver();
        using var response = await Save(client, employeeId, 0, pattern);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("permission_denied", problem.GetProperty("code").GetString());
        Assert.Empty(await ReadCycles(employeeId));
        await using var db = Database();
        Assert.Equal(0, (await db.Employees.SingleAsync(employee => employee.Id == employeeId, Cancellation)).CyclePatternVersion);
    }

    [Fact]
    public async Task Invalid_pattern_returns_fields_and_does_not_write()
    {
        using var client = factory.AsDispatcher();
        var (employeeId, pattern) = await Seed(client);
        using var response = await Save(client, employeeId, 0, pattern with { OnWeeks = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("validation_error", problem.GetProperty("code").GetString());
        Assert.Equal("onWeeks", Assert.Single(problem.GetProperty("fields").EnumerateArray()).GetString());
        Assert.Empty(await ReadCycles(employeeId));
        await using var db = Database();
        Assert.Null((await db.Employees.SingleAsync(employee => employee.Id == employeeId, Cancellation)).CyclePattern);
    }

    [Fact]
    public async Task Owner_can_save_a_pattern()
    {
        using var client = factory.AsOwner();
        var (employeeId, pattern) = await Seed(client);
        using var response = await Save(client, employeeId, 0, pattern);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, (await ReadCycles(employeeId)).Count);
        await using var db = Database();
        Assert.Equal(1, (await db.Employees.SingleAsync(employee => employee.Id == employeeId, Cancellation)).CyclePatternVersion);
    }

    [Fact]
    public async Task Malformed_pattern_json_is_bad_request_and_does_not_write()
    {
        using var client = factory.AsDispatcher();
        var (employeeId, _) = await Seed(client);
        using var content = new StringContent("{\"version\":0,\"pattern\":{\"onWeeks\":\"invalid\"}}", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PutAsync($"/api/v1/people/{employeeId}/cycle-pattern", content, Cancellation);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal("validation_error", problem.GetProperty("code").GetString());
        Assert.Empty(await ReadCycles(employeeId));
        await using var db = Database();
        Assert.Equal(0, (await db.Employees.SingleAsync(employee => employee.Id == employeeId, Cancellation)).CyclePatternVersion);
    }

    [Fact]
    public async Task Membership_contract_counts_active_and_overrun_but_not_planned_or_other_drivers()
    {
        using var client = factory.AsDispatcher();
        var (employeeId, pattern) = await Seed(client);
        using var response = await Save(client, employeeId, 0, pattern);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Database();
        var contract = new EfIsInCycle(db);
        Assert.False(await contract.ForEmployeeAsync(employeeId, Today, Cancellation));
        var cycle = await db.Cycles.SingleAsync(cycle => cycle.EmployeeId == employeeId && cycle.PatternSlot == 0, Cancellation);
        cycle.Status = CycleStatus.Active;
        await db.SaveChangesAsync(Cancellation);
        Assert.True(await contract.ForEmployeeAsync(employeeId, Today, Cancellation));
        Assert.False(await contract.ForEmployeeAsync(employeeId, Today.AddDays(14), Cancellation));
        cycle.Status = CycleStatus.Overrun;
        await db.SaveChangesAsync(Cancellation);
        Assert.True(await contract.ForEmployeeAsync(employeeId, Today.AddDays(14), Cancellation));
        Assert.False(await contract.ForEmployeeAsync(Guid.NewGuid(), Today, Cancellation));
        cycle.Status = CycleStatus.Done;
        await db.SaveChangesAsync(Cancellation);
        Assert.False(await contract.ForEmployeeAsync(employeeId, Today, Cancellation));
    }

    private async Task<(Guid EmployeeId, CyclePattern Pattern)> Seed(HttpClient client)
    {
        var truck = await factory.CreateTruck(client);
        var employeeId = Guid.CreateVersion7();
        await using var db = Database();
        db.Employees.Add(new Employee { Id = employeeId, FirstName = "Fake", LastName = "Driver", Email = "fake@example.invalid", Roles = "[\"driver\"]", Status = EmployeeStatus.Active, StartDate = Today.AddDays(-30) });
        await db.SaveChangesAsync(Cancellation);
        return (employeeId, new CyclePattern(2, 2, Today, truck.Id));
    }

    private Task<HttpResponseMessage> Save(HttpClient client, Guid employeeId, int version, CyclePattern pattern) =>
        client.PutAsJsonAsync($"/api/v1/people/{employeeId}/cycle-pattern", new { version, pattern }, Cancellation);

    private async Task<HttpResponseMessage> RunJob()
    {
        factory.Services.GetRequiredService<IConfiguration>()["Jobs:Secret"] = "cycle-test-secret";
        using var client = factory.Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "cycle-test-secret");
        return await client.GetAsync("/jobs/cycles-rollover", Cancellation);
    }

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "cycle_test_setup" };
    }

    private async Task<List<Cycle>> ReadCycles(Guid employeeId)
    {
        await using var db = Database();
        return await db.Cycles.AsNoTracking().Where(cycle => cycle.EmployeeId == employeeId).OrderBy(cycle => cycle.PatternSlot).ToListAsync(Cancellation);
    }

    private async Task<int> NotificationCount()
    {
        await using var db = Database();
        return await db.Notifications.CountAsync(Cancellation);
    }
}
