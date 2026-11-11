using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FleetOps.Tests;

public sealed partial class PeopleLifecycleTests : IAsyncLifetime
{
    private readonly VehicleApiFactory factory = new();
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-06T07:00:00Z");
    public ValueTask InitializeAsync() => factory.InitializeAsync();
    public ValueTask DisposeAsync() => factory.DisposeAsync();

    private FleetOpsDbContext Database()
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, factory.RuntimeConnectionString);
        return new(options.Options) { AuditActorId = VehicleApiFactory.ActorId, AuditReason = "lifecycle_setup" };
    }

    private static object Person(string? number = null, bool adr = false) => new
    {
        firstName = "Lifecycle", lastName = "Driver", roles = new[] { "driver" },
        phone = "+4915112345678", personnelNumber = number ?? Guid.NewGuid().ToString("N"),
        startDate = Today, employmentType = "full_time", adrRequired = adr
    };

    private async Task<Guid> Employee(EmployeeStatus status = EmployeeStatus.Active, bool adr = false)
    {
        await using var db = Database();
        var employee = new Employee
        {
            Id = Guid.NewGuid(), FirstName = "Lifecycle", LastName = "Driver",
            Roles = "[\"driver\"]", Status = status, StartDate = Today.AddYears(-1),
            PersonnelNumber = Guid.NewGuid().ToString("N"), Phone = "+4915112345678",
            Email = $"{Guid.NewGuid():N}@example.test", AdrRequired = adr
        };
        db.Employees.Add(employee);
        await db.SaveChangesAsync();
        return employee.Id;
    }

    private async Task Depot()
    {
        await using var db = Database();
        Assert.Single(await db.Depots.ToArrayAsync());
    }
    private static async Task<JsonElement> Body(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK, string? code = null)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"Expected {status}, got {response.StatusCode}: {text}");
        var body = JsonSerializer.Deserialize<JsonElement>(text);
        if (code is not null)
        {
            Assert.Equal(code, body.GetProperty("code").GetString());
        }
        return body;
    }

    private async Task AssertStatus(Guid id, EmployeeStatus status)
    {
        await using var db = Database();
        Assert.Equal(status, (await db.Employees.SingleAsync(employee => employee.Id == id)).Status);
    }

    private async Task Documents(Guid id, params DocumentType[] excluded)
    {
        foreach (var type in new[] { DocumentType.IdCard, DocumentType.EmploymentContract, DocumentType.LicenceC_CE, DocumentType.DriverCard, DocumentType.Code95, DocumentType.Medical })
        {
            if (!excluded.Contains(type))
            {
                await using var db = Database();
                db.Documents.Add(new Document
                {
                    OwnerType = DocumentOwnerType.Employee, OwnerId = id, Type = type,
                    Status = DocumentStatus.Verified, CreatedBy = VehicleApiFactory.ActorId,
                    CreatedAt = factory.Clock.GetUtcNow(),
                    ExpiresAt = type == DocumentType.EmploymentContract ? null : Today.AddYears(1)
                });
                await db.SaveChangesAsync();
            }
        }
    }

    [Fact]
    public async Task Hr_adds_a_driver_as_onboarding_and_the_enrolment_sms_goes_out()
    {
        await Depot();
        var sms = new RecordingSms();
        using var actor = factory.AsHr();
        await using var host = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEnrolmentSms>();
            services.AddSingleton<IEnrolmentSms>(sms);
        }));
        using var client = host.CreateClient();
        FleetRequestHeaders.Apply(client);
        var body = await Body(await client.PostAsJsonAsync("/api/v1/people", Person()), HttpStatusCode.Created);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal("onboarding", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("enrolmentSent").GetBoolean());
        var sent = Assert.Single(sms.Sent);
        Assert.Equal("+4915112345678", sent.Phone);
        Assert.Matches("^[0-9]{6}$", sent.Message);
        await using var db = Database();
        var employee = await db.Employees.SingleAsync(employee => employee.Id == id);
        Assert.Equal(EmployeeStatus.Onboarding, employee.Status);
        Assert.NotNull(employee.DepotId);
        Assert.Equal("full_time", employee.EmploymentType);
        var storedCode = Assert.Single(await db.EnrolmentCodes.Where(code => code.EmployeeId == id).ToArrayAsync());
        Assert.Equal(SessionTokens.Hash($"{id}:{sent.Message}"), storedCode.CodeHash);
    }

    private sealed class RecordingSms : IEnrolmentSms
    {
        public List<(string Phone, string Message)> Sent { get; } = [];
        public Task SendAsync(string phone, string code, CancellationToken cancellationToken)
        {
            Sent.Add((phone, code));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Add_person_refuses_missing_fields_with_field_names()
    {
        using var client = factory.AsHr();
        var body = await Body(await client.PostAsJsonAsync("/api/v1/people", new { }), HttpStatusCode.BadRequest, "validation_error");
        var fields = body.GetProperty("fields").EnumerateArray().Select(field => field.GetString()).ToArray();
        foreach (var field in new[] { "firstName", "lastName", "roles", "phone", "startDate", "employmentType" })
        {
            Assert.Contains(field, fields);
        }
        var driverInput = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(Person()))!;
        driverInput.Remove("personnelNumber");
        var driverError = await Body(await client.PostAsJsonAsync("/api/v1/people", driverInput), HttpStatusCode.BadRequest, "validation_error");
        Assert.Equal("personnelNumber", Assert.Single(driverError.GetProperty("fields").EnumerateArray()).GetString());
        var officeInput = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(Person()))!;
        officeInput["roles"] = new[] { "dispatcher" };
        var officeError = await Body(await client.PostAsJsonAsync("/api/v1/people", officeInput), HttpStatusCode.BadRequest, "validation_error");
        Assert.Equal("email", Assert.Single(officeError.GetProperty("fields").EnumerateArray()).GetString());
        await using var db = Database();
        Assert.Empty(await db.Employees.ToArrayAsync());
    }

    [Fact]
    public async Task Add_person_is_refused_for_dispatchers_and_drivers()
    {
        using var dispatcher = factory.AsDispatcher();
        await Body(await dispatcher.PostAsJsonAsync("/api/v1/people", Person()), HttpStatusCode.Forbidden, "permission_denied");
        using var driver = factory.AsDriver();
        await Body(await driver.PostAsJsonAsync("/api/v1/people", Person()), HttpStatusCode.Forbidden, "permission_denied");
        await using var db = Database();
        Assert.Empty(await db.Employees.ToArrayAsync());
    }

    [Fact]
    public async Task Personnel_number_must_be_unique_for_drivers()
    {
        await Depot();
        using var client = factory.AsHr();
        var number = Guid.NewGuid().ToString("N");
        await Body(await client.PostAsJsonAsync("/api/v1/people", Person(number)), HttpStatusCode.Created);
        var refused = await Body(await client.PostAsJsonAsync("/api/v1/people", Person(number)), HttpStatusCode.BadRequest, "validation_error");
        Assert.Contains("personnelNumber", refused.GetProperty("fields").EnumerateArray().Select(field => field.GetString()));
        await using var db = Database();
        Assert.Single(await db.Employees.Where(employee => employee.PersonnelNumber == number).ToArrayAsync());
    }

    [Fact]
    public async Task Dispatcher_people_list_and_profile_have_no_medical_data()
    {
        var id = await Employee();
        await Documents(id, DocumentType.Medical);
        await AlertTestSupport.AddDocument(factory, DocumentOwnerType.Employee, id, DocumentType.Medical, Today.AddDays(-1));
        using var dispatcher = factory.AsDispatcher();
        var list = await Body(await dispatcher.GetAsync("/api/v1/people"));
        var row = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal("document", row.GetProperty("reasonCategory").GetString());
        Assert.Equal("verified", row.GetProperty("worstState").GetString());
        var profile = await Body(await dispatcher.GetAsync($"/api/v1/people/{id}"));
        var medical = Assert.Single(profile.GetProperty("checklist").EnumerateArray(), item => item.GetProperty("type").GetString() == "medical");
        Assert.True(medical.GetProperty("masked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, medical.GetProperty("state").ValueKind);
        Assert.Equal(JsonValueKind.Null, medical.GetProperty("expiresAt").ValueKind);
        await using var db = Database();
        Assert.Equal(Today.AddDays(-1), (await db.Documents.SingleAsync(document => document.OwnerId == id && document.Type == DocumentType.Medical)).ExpiresAt);
    }

    [Fact]
    public async Task Search_finds_a_person_by_name_for_office_roles_only()
    {
        var id = await Employee();
        foreach (var role in new[] { "hr", "owner", "dispatcher", "driver" })
        {
            using var client = role switch { "hr" => factory.AsHr(), "owner" => factory.AsOwner(), "dispatcher" => factory.AsDispatcher(), _ => factory.AsDriver(id) };
            var response = await client.GetAsync("/api/v1/people?search=Lifecycle");
            if (role == "driver")
            {
                await Body(response, HttpStatusCode.Forbidden, "permission_denied");
            }
            else
            {
                var body = await Body(response);
                Assert.Equal(id, Assert.Single(body.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
                var absent = await Body(await client.GetAsync("/api/v1/people?search=NoSuchName"));
                Assert.Empty(absent.GetProperty("items").EnumerateArray());
            }
        }
        await AssertStatus(id, EmployeeStatus.Active);
    }

    [Fact]
    public async Task Checklist_shows_adr_only_for_adr_drivers()
    {
        var normal = await Employee(EmployeeStatus.Onboarding);
        var adr = await Employee(EmployeeStatus.Onboarding, true);
        using var client = factory.AsHr();
        var normalBody = await Body(await client.GetAsync($"/api/v1/people/{normal}"));
        var adrBody = await Body(await client.GetAsync($"/api/v1/people/{adr}"));
        Assert.DoesNotContain(normalBody.GetProperty("checklist").EnumerateArray(), item => item.GetProperty("type").GetString() == "adr_certificate");
        Assert.Contains(adrBody.GetProperty("checklist").EnumerateArray(), item => item.GetProperty("type").GetString() == "adr_certificate");
        await using var db = Database();
        Assert.False((await db.Employees.SingleAsync(employee => employee.Id == normal)).AdrRequired);
        Assert.True((await db.Employees.SingleAsync(employee => employee.Id == adr)).AdrRequired);
    }

    [Fact]
    public async Task Activate_with_everything_verified_makes_the_driver_active()
    {
        var id = await Employee(EmployeeStatus.Onboarding);
        await Documents(id);
        using var client = factory.AsHr();
        var body = await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/activate", new { acceptWarning = false }));
        Assert.Equal("active", body.GetProperty("status").GetString());
        await AssertStatus(id, EmployeeStatus.Active);
    }

    [Fact]
    public async Task Activate_with_only_blocking_documents_missing_needs_the_warning_and_starts_blocked()
    {
        var id = await Employee(EmployeeStatus.Onboarding);
        await Documents(id, DocumentType.Medical);
        using var client = factory.AsHr();
        var refusal = await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/activate", new { acceptWarning = false }), HttpStatusCode.Conflict, "activation_needs_warning");
        Assert.Equal("medical", Assert.Single(refusal.GetProperty("types").EnumerateArray()).GetString());
        await AssertStatus(id, EmployeeStatus.Onboarding);
        await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/activate", new { acceptWarning = true }));
        await AssertStatus(id, EmployeeStatus.Active);
        var detail = await Body(await client.GetAsync($"/api/v1/people/{id}"));
        Assert.False(detail.GetProperty("header").GetProperty("canDrive").GetBoolean());
        await using var db = Database();
        Assert.Contains(await db.AuditEntries.Where(entry => entry.EntityId == id).ToArrayAsync(), entry => entry.FieldName == "activationWarning" && entry.NewValue == "medical");
    }

    [Fact]
    public async Task Activate_with_a_non_blocking_document_missing_is_refused_and_lists_it()
    {
        var id = await Employee(EmployeeStatus.Onboarding);
        await Documents(id, DocumentType.EmploymentContract);
        using var client = factory.AsHr();
        var body = await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/activate", new { acceptWarning = true }), HttpStatusCode.Conflict, "activation_incomplete");
        Assert.Equal("employment_contract", Assert.Single(body.GetProperty("types").EnumerateArray()).GetString());
        await AssertStatus(id, EmployeeStatus.Onboarding);
    }

    [Fact]
    public async Task Suspend_and_reactivate_need_a_reason_and_are_audited()
    {
        var id = await Employee();
        using var client = factory.AsHr();
        await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/suspend", new { reason = "" }), HttpStatusCode.BadRequest, "reason");
        await AssertStatus(id, EmployeeStatus.Active);
        await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/suspend", new { reason = "Safety review" }));
        await AssertStatus(id, EmployeeStatus.Suspended);
        await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/reactivate", new { reason = "" }), HttpStatusCode.BadRequest, "reason");
        await AssertStatus(id, EmployeeStatus.Suspended);
        await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/reactivate", new { reason = "Review complete" }));
        await AssertStatus(id, EmployeeStatus.Active);
        await using var db = Database();
        var audit = await db.AuditEntries.Where(entry => entry.EntityId == id && entry.FieldName == "status").ToArrayAsync();
        Assert.Contains(audit, entry => entry.NewValue == "suspended" && entry.Reason == "Safety review");
        Assert.Contains(audit, entry => entry.NewValue == "active" && entry.Reason == "Review complete");
    }

    [Fact]
    public async Task Office_can_still_enter_a_shift_for_a_suspended_driver()
    {
        var id = await Employee(EmployeeStatus.Suspended);
        using var client = factory.AsDispatcher();
        var body = await Body(await client.PostAsJsonAsync("/api/v1/timesheets/shifts", new AddShiftRequest(id, "Paper entry", new(Today, Start, Start.AddHours(2), null, null, null, null, [new ActivityView(Guid.NewGuid(), ActivityType.OtherWork, Start, Start.AddHours(2))], [])), VehicleApiFactory.JsonOptions));
        var shiftId = body.GetProperty("shiftId").GetGuid();
        await using var db = Database();
        var shift = await db.Shifts.SingleAsync(shift => shift.Id == shiftId);
        Assert.Equal(ShiftSource.Office, shift.Source);
        Assert.Equal("driver_blocked", shift.BlockedAtStart);
        Assert.Equal(id, shift.EmployeeId);
        Assert.Equal(Start.AddHours(2), shift.EndAt);
        Assert.Equal(EmployeeStatus.Suspended, (await db.Employees.SingleAsync(employee => employee.Id == id)).Status);
    }

    [Fact]
    public async Task Rehire_needs_a_reason_and_returns_the_person_active()
    {
        var id = await Employee(EmployeeStatus.Left);
        await using (var db = Database())
        {
            (await db.Employees.SingleAsync(employee => employee.Id == id)).EndDate = Today.AddDays(-1);
            await db.SaveChangesAsync();
        }
        using var client = factory.AsHr();
        await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/rehire", new { reason = "" }), HttpStatusCode.BadRequest, "reason");
        await AssertStatus(id, EmployeeStatus.Left);
        await Body(await client.PostAsJsonAsync($"/api/v1/people/{id}/rehire", new { reason = "New contract" }));
        await using var verify = Database();
        var employee = await verify.Employees.SingleAsync(employee => employee.Id == id);
        Assert.Equal(EmployeeStatus.Active, employee.Status);
        Assert.Null(employee.EndDate);
        Assert.Contains(await verify.AuditEntries.Where(entry => entry.EntityId == id).ToArrayAsync(), entry => entry.Reason == "New contract" && entry.NewValue == "active");
    }
}





