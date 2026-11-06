using System.Net;
using System.Text.Json;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace FleetOps.Tests;

public sealed class SecondFactorSettingTests : IClassFixture<AuthApiFactory>
{
    private const string SessionPath = "/api/v1/auth/session";
    private const string PrivacyPath = "/api/v1/auth/privacy-notice";

    private readonly AuthApiFactory factory;

    public SecondFactorSettingTests(AuthApiFactory fixture)
    {
        factory = fixture;
    }

    [Fact]
    public async Task Demo_with_the_setting_off_lets_owner_in_with_a_password()
    {
        var employee = await factory.CreateEmployee(["owner"]);
        using var client = Client("Demo", false);

        using var signIn = await factory.SignIn(client, employee);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("privacy_notice", NextOf(await BodyOf(signIn)));

        using var privacy = await AcknowledgePrivacy(client);
        using var session = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.OK, privacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Contains("owner", await BodyOf(session), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_with_the_setting_off_lets_hr_in_with_a_password()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = Client("Demo", false);

        using var signIn = await factory.SignIn(client, employee);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("privacy_notice", NextOf(await BodyOf(signIn)));

        using var privacy = await AcknowledgePrivacy(client);
        using var session = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.OK, privacy.StatusCode);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Contains("hr", await BodyOf(session), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_with_the_setting_off_lets_enrolled_owner_in_without_a_code()
    {
        var employee = await factory.CreateEmployee(["owner"]);
        var authenticatorKey = await EnrolAndAcknowledgePrivacy(employee);
        using var client = Client("Demo", false);

        using var signIn = await factory.SignIn(client, employee);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("done", NextOf(await BodyOf(signIn)));
        Assert.Equal(authenticatorKey, await AuthenticatorKey(employee));
    }

    [Fact]
    public async Task Demo_session_is_not_sent_back_to_second_factor()
    {
        var employee = await factory.CreateEmployee(["owner"]);
        await EnrolAndAcknowledgePrivacy(employee);
        using var client = Client("Demo", false);

        using var signIn = await factory.SignIn(client, employee);
        using var session = await factory.Send(client, HttpMethod.Get, SessionPath);

        Assert.Equal("done", NextOf(await BodyOf(signIn)));
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
        Assert.Contains("owner", await BodyOf(session), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Demo_default_still_requires_the_factor()
    {
        var employee = await factory.CreateEmployee(["owner"]);
        using var client = Client("Demo", null);

        using var signIn = await factory.SignIn(client, employee);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("enrol_second_factor", NextOf(await BodyOf(signIn)));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Testing")]
    public async Task Setting_off_outside_Demo_is_ignored(string environment)
    {
        var employee = await factory.CreateEmployee(["owner"]);
        using var client = Client(environment, false);

        using var signIn = await factory.SignIn(client, employee);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("enrol_second_factor", NextOf(await BodyOf(signIn)));
    }

    [Fact]
    public async Task Setting_off_does_not_change_a_dispatcher()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = Client("Demo", false);

        using var signIn = await factory.SignIn(client, employee);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("privacy_notice", NextOf(await BodyOf(signIn)));
    }

    [Fact]
    public async Task Wrong_password_still_fails_with_the_setting_off()
    {
        var employee = await factory.CreateEmployee(["owner"]);
        using var client = Client("Demo", false);

        using var signIn = await factory.SignIn(client, employee, "wrong password");

        Assert.Equal(HttpStatusCode.Unauthorized, signIn.StatusCode);
        Assert.Equal("sign_in_failed", CodeOf(await BodyOf(signIn)));
    }

    private HttpClient Client(string environment, bool? requireSecondFactor)
    {
        var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            if (requireSecondFactor.HasValue)
            {
                builder.UseSetting("Auth:RequireSecondFactor", requireSecondFactor.Value.ToString().ToLowerInvariant());
            }
            else
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.Sources.Clear();
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:FleetOps"] = factory.RuntimeConnectionString,
                        ["Auth:PublicOrigin"] = factory.Origin,
                    });
                });
            }
        });
        return host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri(factory.Origin),
            HandleCookies = true,
        });
    }

    private async Task<HttpResponseMessage> AcknowledgePrivacy(HttpClient client)
    {
        return await factory.Send(client, HttpMethod.Post, PrivacyPath, new AuthEndpoints.PrivacyRequest(AuthOptions.PrivacyVersion));
    }

    private async Task<string> EnrolAndAcknowledgePrivacy(OfficeUser employee)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OfficeUser>>();
        var current = await users.FindByIdAsync(employee.Id.ToString()) ?? throw new InvalidOperationException("User not found");
        await users.ResetAuthenticatorKeyAsync(current);
        current.TwoFactorEnabled = true;
        var result = await users.UpdateAsync(current);
        Assert.True(result.Succeeded, string.Join(";", result.Errors.Select(error => error.Code)));
        db.PrivacyAcknowledgements.Add(new PrivacyAcknowledgement
        {
            Id = Guid.NewGuid(),
            EmployeeId = employee.EmployeeId,
            Version = AuthOptions.PrivacyVersion,
            AcknowledgedAt = factory.Clock.GetUtcNow(),
        });
        await db.SaveChangesAsync();
        return await users.GetAuthenticatorKeyAsync(current) ?? throw new InvalidOperationException("Authenticator key not found");
    }

    private async Task<string> AuthenticatorKey(OfficeUser employee)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OfficeUser>>();
        var current = await users.FindByIdAsync(employee.Id.ToString()) ?? throw new InvalidOperationException("User not found");
        return await users.GetAuthenticatorKeyAsync(current) ?? throw new InvalidOperationException("Authenticator key not found");
    }

    private static async Task<string> BodyOf(HttpResponseMessage response) => await response.Content.ReadAsStringAsync();

    private static string NextOf(string body) => JsonDocument.Parse(body).RootElement.GetProperty("next").GetString()!;

    private static string CodeOf(string body) => JsonDocument.Parse(body).RootElement.GetProperty("code").GetString()!;
}
