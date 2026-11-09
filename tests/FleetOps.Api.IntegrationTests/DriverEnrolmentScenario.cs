using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.People;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

internal sealed class DriverEnrolmentScenario : IAsyncDisposable
{
    public const string OfficePhone = "+493012345678";
    public AuthApiFactory Factory { get; } = new();
    private WebApplicationFactory<Program> host = null!;
    public OfficeUser Driver { get; private set; } = null!;
    public OfficeUser Actor { get; private set; } = null!;
    public HttpClient Office { get; private set; } = null!;
    public string Number { get; } = Guid.NewGuid().ToString("N");

    public static async Task<DriverEnrolmentScenario> Create(string role = "hr", bool testInbox = true)
    {
        var scenario = new DriverEnrolmentScenario();
        await scenario.Factory.InitializeAsync();
        scenario.host = scenario.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.PostConfigure<EnrolmentOptions>(options =>
            {
                options.TestInbox = testInbox;
                options.OfficePhone = OfficePhone;
            })));
        scenario.Driver = await scenario.Factory.CreateEmployee(["driver"]);
        scenario.Actor = await scenario.Factory.CreateEmployee([role]);
        await scenario.EditDriver(employee =>
        {
            employee.PersonnelNumber = scenario.Number;
            employee.Phone = "+4915112345678";
        });
        var token = await scenario.AddSession(scenario.Actor, role == "driver" ? "driver" : "office");
        scenario.Office = scenario.Client();
        scenario.Office.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"{AuthEndpoints.CookieName}={token}");
        return scenario;
    }

    public HttpClient Client() => host.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(Factory.Origin),
        AllowAutoRedirect = false,
        HandleCookies = true
    });

    public Task<HttpResponseMessage> Send() =>
        Factory.Send(Office, HttpMethod.Post, $"/api/v1/people/{Driver.EmployeeId}/enrolment");

    public async Task<string> Issue()
    {
        using var response = await Send();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var inbox = body.GetProperty("testInbox");
        Assert.Equal(Number, inbox.GetProperty("personnelNumber").GetString());
        return inbox.GetProperty("code").GetString()!;
    }

    public async Task<HttpResponseMessage> Enrol(string code, HttpClient? client = null, string? number = null)
    {
        if (client is not null)
            return await Factory.Send(client, HttpMethod.Post, "/api/v1/auth/driver/enrol", new { personnelNumber = number ?? Number, code });
        using var anonymous = Client();
        return await Factory.Send(anonymous, HttpMethod.Post, "/api/v1/auth/driver/enrol", new { personnelNumber = number ?? Number, code });
    }

    public Task<HttpResponseMessage> Privacy(HttpClient client) =>
        Factory.Send(client, HttpMethod.Post, "/api/v1/auth/privacy-notice", new { version = AuthOptions.PrivacyVersion });

    public async Task<HttpClient> FullDriver()
    {
        var client = Client();
        using var enrolled = await Enrol(await Issue(), client);
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        using var privacy = await Privacy(client);
        Assert.Equal(HttpStatusCode.OK, privacy.StatusCode);
        return client;
    }

    public async Task Database(Func<FleetOpsDbContext, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>();
        await action(db);
    }

    public Task EditDriver(Action<Employee> edit) => Database(async db =>
    {
        var employee = await db.Employees.SingleAsync(employee => employee.Id == Driver.EmployeeId);
        edit(employee);
        await db.SaveChangesAsync();
    });

    public async Task<List<EnrolmentCode>> Codes()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>().EnrolmentCodes.AsNoTracking()
            .Where(code => code.EmployeeId == Driver.EmployeeId).ToListAsync();
    }

    public async Task<List<OfficeSession>> Sessions()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>().OfficeSessions.AsNoTracking()
            .Where(session => session.EmployeeId == Driver.EmployeeId).ToListAsync();
    }

    public async Task<string> AddSession(OfficeUser user, string device)
    {
        var token = SessionTokens.New();
        await Database(async db =>
        {
            var now = Factory.Clock.GetUtcNow();
            db.OfficeSessions.Add(new OfficeSession
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                EmployeeId = user.EmployeeId,
                TokenHash = SessionTokens.Hash(token),
                Device = device,
                Stage = SessionStage.Full,
                CreatedAt = now,
                StageChangedAt = now,
                LastSeenAt = now,
                StepUpAt = now
            });
            await db.SaveChangesAsync();
        });
        return token;
    }

    public async ValueTask DisposeAsync()
    {
        Office.Dispose();
        await host.DisposeAsync();
        await Factory.DisposeAsync();
    }
}
