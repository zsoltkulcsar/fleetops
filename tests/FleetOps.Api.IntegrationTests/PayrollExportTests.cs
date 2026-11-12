using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FleetOps.Migrations;
using FleetOps.Modules.Payroll;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public sealed class PayrollExportTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private readonly Guid employeeId = Guid.NewGuid();
    private static readonly DateOnly Month = new(2026, 11, 1);
    private HttpClient hr = null!;
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.InitializeAsync();
        factory.FreshStepUp = true;
        hr = factory.AsHr();
        factory.Services.GetRequiredService<IConfiguration>()["Company:Timezone"] = "Europe/Berlin";
        await using var db = Database();
        db.Employees.Add(new Employee { Id = VehicleApiFactory.ActorId, FirstName = "Helen", LastName = "HR", Email = "hr@example.invalid", Roles = "[\"hr\"]", StartDate = Month });
        db.Employees.Add(new Employee { Id = employeeId, FirstName = "Dana", LastName = "Driver", Email = "payroll@example.invalid", PersonnelNumber = "P-001", Roles = "[\"driver\"]", StartDate = Month, EndDate = Month.AddDays(20) });
        await db.SaveChangesAsync(Cancellation);
    }

    public async ValueTask DisposeAsync()
    {
        hr.Dispose();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task Readiness_shows_approved_waiting_missing_and_final_month()
    {
        await Seed();
        await Seed(3, ShiftStatus.Submitted);
        using var owner = factory.AsOwner();
        var truck = await factory.CreateTruck(owner);
        using var restoredHr = factory.AsHr();
        await using (var setup = Database())
        {
            setup.Cycles.Add(new Cycle { EmployeeId = employeeId, VehicleId = truck.Id, From = Month.AddDays(1), To = Month.AddDays(6), Status = CycleStatus.Active, Source = CycleSource.Manual });
            await setup.SaveChangesAsync(Cancellation);
        }
        var person = Assert.Single((await Get<PayrollReadiness>("readiness")).People);
        Assert.Equal(1, person.Approved);
        Assert.Equal(1, person.Waiting);
        Assert.Contains(Month.AddDays(2), person.Missing);
        Assert.True(person.FinalMonth);
        Assert.False(person.Ready);
        await using var db = Database();
        Assert.Equal(2, await db.Shifts.CountAsync(Cancellation));
        Assert.Empty(await db.PayrollExports.ToArrayAsync(Cancellation));
    }

    [Fact]
    public async Task Preview_locks_nothing()
    {
        var shift = await Seed();
        var preview = await Preview();
        Assert.Equal(new PayrollShiftVersion(shift.Id, 1), Assert.Single(preview.Fingerprint));
        Assert.Equal(8m, Assert.Single(preview.Totals).DrivingHours);
        await using var db = Database();
        var stored = await db.Shifts.SingleAsync(Cancellation);
        Assert.Equal(ShiftStatus.Approved, stored.Status);
        Assert.Equal(1, stored.Version);
        Assert.Null(stored.ExportId);
        Assert.Empty(await db.PayrollExports.ToArrayAsync(Cancellation));
    }

    [Fact]
    public async Task Generate_needs_step_up()
    {
        await Seed();
        var preview = await Preview();
        factory.FreshStepUp = false;
        using var response = await GenerateResponse(preview);
        await Error(response, HttpStatusCode.Forbidden, "step_up_required");
        await AssertUnexported(1);
    }

    [Fact]
    public async Task Generate_locks_every_shift_at_its_preview_version()
    {
        await Seed();
        await Seed(3);
        var preview = await Preview();
        var export = await Generate(preview);
        Assert.Equal(preview.Fingerprint, export.Shifts);
        await using var db = Database();
        foreach (var expected in preview.Fingerprint)
        {
            var stored = await db.Shifts.SingleAsync(row => row.Id == expected.ShiftId, Cancellation);
            Assert.Equal(ShiftStatus.Exported, stored.Status);
            Assert.Equal(expected.Version + 1, stored.Version);
            Assert.Equal(export.Id, stored.ExportId);
        }
        var file = await db.PayrollExports.SingleAsync(Cancellation);
        Assert.Equal("generated", file.Status);
        Assert.Equal(export.Checksum, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(factory.ObjectStore.Contents[file.FileKey])));
    }

    [Fact]
    public async Task Generate_fails_whole_when_a_shift_changed_and_lists_it()
    {
        var changed = await Seed();
        await Seed(3);
        var preview = await Preview();
        await using (var setup = Database())
        {
            (await setup.Shifts.SingleAsync(row => row.Id == changed.Id, Cancellation)).Version++;
            await setup.SaveChangesAsync(Cancellation);
        }
        using var response = await GenerateResponse(preview);
        var body = await Error(response, HttpStatusCode.Conflict, "export_changed");
        var listed = Assert.Single(body.GetProperty("shifts").EnumerateArray());
        Assert.Equal(changed.Id, listed.GetProperty("shiftId").GetGuid());
        Assert.Equal(2, listed.GetProperty("version").GetInt32());
        await AssertUnexported(2);
        Assert.Empty(factory.ObjectStore.Uploaded);
    }

    [Fact]
    public async Task Csv_holds_hours_nights_absences_flags_and_no_money()
    {
        // Exercise the real format with all columns, including values absent from a normal shift.
        var day = new PayrollDay(employeeId, "P-001", "Dana Driver", Month, 8.5m, 1.25m, 2m,
            new Dictionary<string, decimal> { ["DE"] = 1 }, new Dictionary<string, decimal> { ["vacation"] = 0.5m }, true, true);
        var csv = Encoding.UTF8.GetString(new CsvGenericPayrollFormat().Write([day]));
        Assert.Equal("personnel_number;name;date;driving_h;other_work_h;availability_h;nights_country;absence_type;sunday;holiday", csv.TrimStart('\uFEFF').Split('\r', '\n')[0]);
        Assert.Contains("\"P-001\";\"Dana Driver\";2026-11-01;8,5;1,25;2;\"DE:1\";\"vacation:0,5\";1;1", csv);
        await Seed();
        var export = await Generate(await Preview());
        await using var db = Database();
        var stored = await db.PayrollExports.SingleAsync(Cancellation);
        Assert.Equal(export.Id, stored.Id);
        Assert.Contains(";8;0;0;", Encoding.UTF8.GetString(factory.ObjectStore.Contents[stored.FileKey]));
    }

    [Fact]
    public async Task Datev_generate_is_refused_until_the_layout_is_confirmed()
    {
        await Seed();
        using var response = await GenerateResponse(await Preview(), "datev_lohn");
        await Error(response, HttpStatusCode.Conflict, "datev_layout_not_confirmed");
        await AssertUnexported(1);
        Assert.Empty(factory.ObjectStore.Uploaded);
    }

    [Fact]
    public async Task Mark_sent_keeps_every_version()
    {
        await Seed();
        var original = await Generate(await Preview());
        await Sent(original);
        var late = await Seed(3);
        var supplement = await Generate(await Preview());
        await Sent(supplement);
        var history = await Get<PayrollHistory>("exports");
        Assert.Equal(2, history.Exports.Count);
        Assert.All(history.Exports, row => Assert.Equal("sent", row.Status));
        await using var db = Database();
        var first = await db.PayrollExports.SingleAsync(row => row.Id == original.Id, Cancellation);
        Assert.Equal(original.Checksum, first.Checksum);
        Assert.Equal(original.Shifts, JsonSerializer.Deserialize<PayrollShiftVersion[]>(first.ShiftVersionsJson));
        Assert.True(factory.ObjectStore.Contents.ContainsKey(first.FileKey));
        Assert.Equal(supplement.Id, (await db.Shifts.SingleAsync(row => row.Id == late.Id, Cancellation)).ExportId);
    }

    [Fact]
    public async Task Discard_unlocks_the_shifts_and_is_audited()
    {
        await Seed();
        await Seed(3);
        var export = await Generate(await Preview());
        using var response = await hr.PostAsJsonAsync($"/api/v1/payroll/exports/{export.Id}/discard", new { reason = "Wrong period" }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var db = Database();
        Assert.Equal("discarded", (await db.PayrollExports.SingleAsync(Cancellation)).Status);
        Assert.All(await db.Shifts.ToArrayAsync(Cancellation), shift => { Assert.Equal(ShiftStatus.Approved, shift.Status); Assert.Null(shift.ExportId); Assert.Equal(3, shift.Version); });
        Assert.True(await db.AuditEntries.AnyAsync(row => row.EntityId == export.Id && row.NewValue == "discarded" && row.Reason == "Wrong period", Cancellation));
    }

    [Fact]
    public async Task Late_approved_shifts_go_as_a_supplement()
    {
        var first = await Seed();
        var regular = await Generate(await Preview());
        await Sent(regular);
        var late = await Seed(3);
        var supplement = await Generate(await Preview());
        Assert.Equal("supplement", supplement.Kind);
        Assert.Equal(late.Id, Assert.Single(supplement.Shifts).ShiftId);
        await using var db = Database();
        Assert.Equal(regular.Id, (await db.Shifts.SingleAsync(row => row.Id == first.Id, Cancellation)).ExportId);
        Assert.Equal("supplement", (await db.PayrollExports.SingleAsync(row => row.Id == supplement.Id, Cancellation)).Kind);
    }

    [Fact]
    public async Task Reopen_before_sent_supersedes_the_file()
    {
        var target = await Seed();
        await Seed(3);
        var export = await Generate(await Preview());
        await Reopen(target.Id);
        await using var db = Database();
        Assert.Equal("superseded", (await db.PayrollExports.SingleAsync(Cancellation)).Status);
        var reopened = await db.Shifts.SingleAsync(row => row.Id == target.Id, Cancellation);
        Assert.Equal(ShiftStatus.Submitted, reopened.Status);
        Assert.Null(reopened.ExportId);
        Assert.Equal(ShiftStatus.Approved, (await db.Shifts.SingleAsync(row => row.Id != target.Id, Cancellation)).Status);
        Assert.True(factory.ObjectStore.Contents.ContainsKey((await db.PayrollExports.SingleAsync(row => row.Id == export.Id, Cancellation)).FileKey));
    }

    [Fact]
    public async Task Reopen_after_sent_produces_a_correction_with_deltas()
    {
        var target = await Seed();
        var original = await Generate(await Preview());
        await Sent(original);
        await Reopen(target.Id);
        await using (var setup = Database())
        {
            var shift = await setup.Shifts.Include(row => row.Activities).SingleAsync(Cancellation);
            shift.Activities[0].EndAt = shift.Activities[0].EndAt!.Value.AddHours(1);
            shift.EndAt = shift.EndAt!.Value.AddHours(1);
            shift.Status = ShiftStatus.Approved;
            shift.Version++;
            await setup.SaveChangesAsync(Cancellation);
        }
        var correction = await Generate(await Preview());
        Assert.Equal("correction", correction.Kind);
        Assert.Equal(original.Id, correction.CorrectionOf);
        var delta = Assert.Single(correction.Deltas);
        Assert.Equal(target.Date, delta.Date);
        Assert.Equal(8m, delta.Old!.DrivingHours);
        Assert.Equal(9m, delta.New!.DrivingHours);
        await using var db = Database();
        Assert.Equal("sent", (await db.PayrollExports.SingleAsync(row => row.Id == original.Id, Cancellation)).Status);
        var stored = await db.PayrollExports.SingleAsync(row => row.Id == correction.Id, Cancellation);
        Assert.Contains(";1;0;0;", Encoding.UTF8.GetString(factory.ObjectStore.Contents[stored.FileKey]));
        Assert.Equal(correction.Id, (await db.Shifts.SingleAsync(Cancellation)).ExportId);
    }

    [Fact]
    public async Task Dispatchers_and_drivers_cannot_export()
    {
        await Seed();
        var preview = await Preview();
        foreach (var role in new[] { "dispatcher", "driver" })
        {
            using var client = role == "dispatcher" ? factory.AsDispatcher() : factory.AsDriver(employeeId);
            using var response = await client.PostAsJsonAsync("/api/v1/payroll/exports", new GeneratePayrollCommand(Month, "csv_generic", preview.Fingerprint), Cancellation);
            await Error(response, HttpStatusCode.Forbidden, "permission_denied");
            using var read = await client.GetAsync("/api/v1/payroll/readiness?month=2026-11-01", Cancellation);
            await Error(read, HttpStatusCode.Forbidden, "permission_denied");
        }
        await AssertUnexported(1);
    }

    private async Task<Shift> Seed(int day = 1, ShiftStatus status = ShiftStatus.Approved)
    {
        var start = new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero).AddDays(day);
        var shift = ShiftRules.Start(Guid.NewGuid(), employeeId, Month.AddDays(day), start, null);
        ShiftRules.End(shift, start.AddHours(8));
        shift.Activities.Clear();
        shift.Activities.Add(new ShiftActivity { ShiftId = shift.Id, Type = ActivityType.Driving, StartAt = start, EndAt = start.AddHours(8), RecordedAt = start });
        shift.Version = 1;
        shift.Status = status;
        shift.ApprovedBy = status == ShiftStatus.Approved ? VehicleApiFactory.ActorId : null;
        shift.ApprovedAt = status == ShiftStatus.Approved ? start : null;
        await using var db = Database();
        db.Shifts.Add(shift);
        await db.SaveChangesAsync(Cancellation);
        return shift;
    }

    private Task<PayrollPreview> Preview() => Get<PayrollPreview>("preview");
    private async Task<T> Get<T>(string path)
    {
        using var response = await hr.GetAsync($"/api/v1/payroll/{path}?month=2026-11-01", Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(VehicleApiFactory.JsonOptions, Cancellation))!;
    }
    private Task<HttpResponseMessage> GenerateResponse(PayrollPreview preview, string format = "csv_generic") =>
        hr.PostAsJsonAsync("/api/v1/payroll/exports", new GeneratePayrollCommand(Month, format, preview.Fingerprint), Cancellation);
    private async Task<PayrollExportView> Generate(PayrollPreview preview)
    {
        using var response = await GenerateResponse(preview);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PayrollExportView>(VehicleApiFactory.JsonOptions, Cancellation))!;
    }
    private async Task Sent(PayrollExportView export)
    {
        using var response = await hr.PostAsJsonAsync($"/api/v1/payroll/exports/{export.Id}/sent", new { sentDate = "2026-11-06", note = "Delivered" }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private async Task Reopen(Guid id)
    {
        using var response = await hr.PostAsJsonAsync($"/api/v1/shifts/{id}/reopen", new { version = 2, reason = "Correct driving time" }, Cancellation);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    private async Task<JsonElement> Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Cancellation);
        Assert.Equal(code, body.GetProperty("code").GetString());
        return body;
    }
    private async Task AssertUnexported(int count)
    {
        await using var db = Database();
        Assert.Empty(await db.PayrollExports.ToArrayAsync(Cancellation));
        var shifts = await db.Shifts.ToArrayAsync(Cancellation);
        Assert.Equal(count, shifts.Length);
        Assert.All(shifts, shift => { Assert.Equal(ShiftStatus.Approved, shift.Status); Assert.Null(shift.ExportId); });
    }
    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new FleetOpsDbContext(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "payroll_test_setup" };
    }
}


