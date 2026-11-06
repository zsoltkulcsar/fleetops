using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.Migrations;
using FleetOps.People;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

// Origin guard, roles, step-up, lockout enumeration, input handling, and the shared helpers.
public sealed partial class SignInTests
{
    // ------------------------------------------------------------------ origin guard

    [Fact]
    public async Task Foreign_origin_is_refused_on_an_unsafe_request()
    {
        var outcome = await SendUnsafeRequests("https://evil.example", includeCustomHeader: true);

        Assert.Equal(HttpStatusCode.Forbidden, outcome.SignInStatus);
        Assert.Equal("origin_refused", outcome.SignInCode);
        Assert.Equal(HttpStatusCode.Forbidden, outcome.SignOutStatus);
        Assert.Equal("origin_refused", outcome.SignOutCode);
        Assert.Null(outcome.RevokedAt);
    }

    [Fact]
    public async Task Missing_origin_is_refused_on_an_unsafe_request()
    {
        var outcome = await SendUnsafeRequests(origin: null, includeCustomHeader: true);

        Assert.Equal(HttpStatusCode.Forbidden, outcome.SignInStatus);
        Assert.Equal("origin_refused", outcome.SignInCode);
        Assert.Equal(HttpStatusCode.Forbidden, outcome.SignOutStatus);
        Assert.Equal("origin_refused", outcome.SignOutCode);
        Assert.Null(outcome.RevokedAt);
    }

    [Fact]
    public async Task Missing_custom_header_is_refused_on_an_unsafe_request()
    {
        var outcome = await SendUnsafeRequests(factory.Origin, includeCustomHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, outcome.SignInStatus);
        Assert.Equal("origin_refused", outcome.SignInCode);
        Assert.Equal(HttpStatusCode.Forbidden, outcome.SignOutStatus);
        Assert.Equal("origin_refused", outcome.SignOutCode);
        Assert.Null(outcome.RevokedAt);
    }

    [Fact]
    public async Task Origin_guard_uses_the_configured_public_origin_behind_a_proxy()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var plainHttpClient = factory.PlainClient("http://localhost");
        var credentials = new AuthEndpoints.SignInRequest(employee.Email!, AuthApiFactory.Password);

        using var matchesConfiguration = await factory.Send(plainHttpClient, HttpMethod.Post, SignInPath, credentials, origin: "https://localhost");
        using var matchesRequestScheme = await factory.Send(plainHttpClient, HttpMethod.Post, SignInPath, credentials, origin: "http://localhost");
        using var foreign = await factory.Send(plainHttpClient, HttpMethod.Post, SignInPath, credentials, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.OK, matchesConfiguration.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, matchesRequestScheme.StatusCode);
        Assert.Equal("origin_refused", CodeOf(await BodyOf(matchesRequestScheme)));
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        Assert.Equal("origin_refused", CodeOf(await BodyOf(foreign)));
    }

    [Fact]
    public async Task Unsafe_requests_are_refused_without_a_configured_origin()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        await using var unconfigured = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:PublicOrigin"] = "" })));
        using var client = unconfigured.CreateClient(OptionsFor(factory.Origin));
        var credentials = new AuthEndpoints.SignInRequest(employee.Email!, AuthApiFactory.Password);

        using var withOriginAndHeader = await factory.Send(client, HttpMethod.Post, SignInPath, credentials);
        using var withoutOrigin = await factory.Send(client, HttpMethod.Post, SignInPath, credentials, includeOrigin: false);
        using var withoutAnything = await factory.Send(client, HttpMethod.Post, SignInPath, credentials, includeOrigin: false, includeCustomHeader: false);

        Assert.Equal(HttpStatusCode.Forbidden, withOriginAndHeader.StatusCode);
        Assert.Equal("origin_refused", CodeOf(await BodyOf(withOriginAndHeader)));
        Assert.Equal(HttpStatusCode.Forbidden, withoutOrigin.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withoutAnything.StatusCode);
        Assert.Equal(0, await Db(db => db.OfficeSessions.CountAsync(row => row.UserId == employee.Id)));
    }

    [Fact]
    public async Task Get_requests_are_not_blocked_by_the_origin_guard()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);

        using var response = await factory.Send(client, HttpMethod.Get, SessionPath, includeOrigin: false, includeCustomHeader: false);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(employee.EmployeeId.ToString(), await BodyOf(response));
    }

    // ------------------------------------------------------------------ roles and step-up

    [Fact]
    public async Task Dispatcher_command_actor_comes_from_the_session_not_the_payload()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        var other = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);

        using var response = await factory.Send(client, HttpMethod.Post, "/__tests/dispatcher-command", new { payloadActor = other.EmployeeId });

        var body = await BodyOf(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(employee.EmployeeId.ToString(), body);
        Assert.DoesNotContain(other.EmployeeId.ToString(), body);
    }

    [Fact]
    public async Task Roles_change_takes_effect_on_the_next_request()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);

        await SetRoles(employee, "dispatcher");

        using var response = await factory.Send(client, HttpMethod.Get, SessionPath);
        var body = await BodyOf(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("dispatcher", body);
        Assert.DoesNotContain("hr", body);
    }

    [Fact]
    public async Task Demotion_takes_effect_on_the_next_request()
    {
        var employee = await factory.CreateEmployee(["owner"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        var vehiclePath = $"/api/v1/fleet/vehicles/{Guid.NewGuid()}";
        using var asOwner = await factory.Send(client, HttpMethod.Get, vehiclePath);
        Assert.Equal(HttpStatusCode.NotFound, asOwner.StatusCode);

        await SetRoles(employee, "driver");

        using var session = await factory.Send(client, HttpMethod.Get, SessionPath);
        using var asDriver = await factory.Send(client, HttpMethod.Get, vehiclePath);
        var roles = JsonDocument.Parse(await BodyOf(session)).RootElement.GetProperty("roles")
            .EnumerateArray().Select(role => role.GetString()).ToArray();
        Assert.Equal(["driver"], roles);
        Assert.Equal(HttpStatusCode.Forbidden, asDriver.StatusCode);
        Assert.Equal("permission_denied", CodeOf(await BodyOf(asDriver)));
    }

    [Fact]
    public async Task Promotion_to_owner_requires_a_second_factor_before_access()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        using var beforePromotion = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.OK, beforePromotion.StatusCode);

        await SetRoles(employee, "owner");

        using var session = await factory.Send(client, HttpMethod.Get, SessionPath);
        using var vehicle = await factory.Send(client, HttpMethod.Get, $"/api/v1/fleet/vehicles/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, vehicle.StatusCode);
        var stage = (await LatestSession(employee)).Stage;
        Assert.Contains(stage, new[] { SessionStage.PendingEnrolment, SessionStage.PendingSecondFactor });

        using var secondClient = factory.Client;
        await CompleteSignIn(secondClient, employee);
        using var afterFactor = await factory.Send(secondClient, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.OK, afterFactor.StatusCode);
        Assert.Contains("owner", await BodyOf(afterFactor));
    }

    [Fact]
    public async Task Step_up_action_is_refused_without_a_fresh_second_factor()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);

        using var response = await factory.Send(client, HttpMethod.Post, StepUpProbePath, new AuthEndpoints.SecondFactorRequest("000000"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("step_up_required", CodeOf(await BodyOf(response)));
    }

    [Fact]
    public async Task Step_up_action_passes_after_step_up()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));

        using var stepUp = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(await factory.CurrentTotp(employee)));

        Assert.Equal(HttpStatusCode.OK, stepUp.StatusCode);
        using var command = await factory.Send(client, HttpMethod.Post, StepUpProbePath);
        Assert.Equal(HttpStatusCode.OK, command.StatusCode);
        Assert.NotNull((await LatestSession(employee)).StepUpAt);
    }

    [Fact]
    public async Task Step_up_code_cannot_be_reused()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        var codeSpentAtSignIn = await factory.CurrentTotp(employee);
        using var sameStep = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(codeSpentAtSignIn));
        Assert.Equal(HttpStatusCode.Unauthorized, sameStep.StatusCode);
        Assert.Equal("second_factor_failed", CodeOf(await BodyOf(sameStep)));

        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        var freshCode = await factory.CurrentTotp(employee);
        using var firstUse = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(freshCode));
        using var secondUse = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(freshCode));
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        using var nextStepReplay = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(freshCode));

        Assert.Equal(HttpStatusCode.OK, firstUse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondUse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, nextStepReplay.StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        var newestCode = await factory.CurrentTotp(employee);
        using var newCode = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(newestCode));
        Assert.Equal(HttpStatusCode.OK, newCode.StatusCode);
    }

    [Fact]
    public async Task Future_window_totp_code_cannot_be_replayed_at_sign_in()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var first = factory.PlainClient();
        using var signIn = await factory.SignIn(first, employee);
        Assert.Equal("enrol_second_factor", NextOf(await BodyOf(signIn)));
        var pendingToken = SessionCookieValue(signIn);
        using var enrol = await factory.Send(first, HttpMethod.Post, "/api/v1/auth/second-factor/enrol", sessionToken: pendingToken);
        Assert.Equal(HttpStatusCode.OK, enrol.StatusCode);

        var currentStep = factory.Clock.GetUtcNow().ToUnixTimeSeconds() / 30;
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        var futureCode = await factory.CurrentTotp(employee);
        var futureStep = factory.Clock.GetUtcNow().ToUnixTimeSeconds() / 30;
        factory.Clock.Advance(TimeSpan.FromSeconds(-30));
        using var firstFactor = await factory.Send(first, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(futureCode), sessionToken: pendingToken);

        using var second = factory.PlainClient();
        using var secondSignIn = await factory.SignIn(second, employee);
        using var replay = await factory.Send(second, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(futureCode));

        Assert.Equal(HttpStatusCode.OK, firstFactor.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("second_factor_failed", CodeOf(await BodyOf(replay)));
        Assert.Equal(currentStep + 1, futureStep);
        Assert.Equal(futureStep, (await LoadUser(employee)).LastAcceptedTotpStep);
    }

    [Fact]
    public async Task Future_window_step_up_code_cannot_be_reused()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        var currentStep = factory.Clock.GetUtcNow().ToUnixTimeSeconds() / 30;
        await CompleteSignIn(client, employee);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        var futureCode = await factory.CurrentTotp(employee);

        using var firstUse = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(futureCode));
        using var secondUse = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(futureCode));

        Assert.Equal(HttpStatusCode.OK, firstUse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, secondUse.StatusCode);
        Assert.Equal("second_factor_failed", CodeOf(await BodyOf(secondUse)));
        Assert.Equal(currentStep + 1, (await LoadUser(employee)).LastAcceptedTotpStep);
    }

    [Fact]
    public async Task Step_up_expires_after_its_window()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        using var stepUp = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(await factory.CurrentTotp(employee)));
        Assert.Equal(HttpStatusCode.OK, stepUp.StatusCode);

        factory.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        using var command = await factory.Send(client, HttpMethod.Post, StepUpProbePath);
        Assert.Equal(HttpStatusCode.Forbidden, command.StatusCode);
        Assert.Equal("step_up_required", CodeOf(await BodyOf(command)));
    }

    [Fact]
    public async Task Second_factor_failures_lock_the_account_across_sessions()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var first = factory.Client;
        using var firstSignIn = await factory.SignIn(first, employee);
        using var second = factory.Client;
        using var secondSignIn = await factory.SignIn(second, employee);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var response = await factory.Send(first, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest("000000"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var response = await factory.Send(second, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest("000000"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var user = await LoadUser(employee);
        Assert.Equal(5, user.FailedSecondFactorAttempts);
        Assert.True(user.LockoutEnd > factory.Clock.GetUtcNow());
        using var lockedSignIn = await factory.SignIn(factory.Client, employee);
        Assert.Equal(HttpStatusCode.Locked, lockedSignIn.StatusCode);
        Assert.Equal("account_locked", CodeOf(await BodyOf(lockedSignIn)));
    }

    [Fact]
    public async Task A_correct_password_does_not_clear_second_factor_failures()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        using var signIn = await factory.SignIn(client, employee);
        using var wrong = await factory.Send(client, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest("000000"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var correctPassword = await factory.SignIn(factory.Client, employee);
        Assert.Equal(HttpStatusCode.OK, correctPassword.StatusCode);
        Assert.Equal(1, (await LoadUser(employee)).FailedSecondFactorAttempts);
    }

    [Fact]
    public async Task Parallel_wrong_codes_are_all_counted()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var bootstrap = factory.Client;
        await CompleteSignIn(bootstrap, employee);
        var clients = Enumerable.Range(0, 39).Select(_ => factory.Client).ToArray();
        foreach (var client in clients)
        {
            using var signIn = await factory.SignIn(client, employee);
            Assert.Equal("second_factor", NextOf(await BodyOf(signIn)));
        }
        var requests = clients.Select(client => factory.Send(client, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest("000000")));
        var responses = await Task.WhenAll(requests);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Equal(5, (await LoadUser(employee)).FailedSecondFactorAttempts);
        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task Parallel_wrong_passwords_are_all_counted()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        var requests = Enumerable.Range(0, 39).Select(_ => factory.SignIn(factory.Client, employee, "wrong"));
        var responses = await Task.WhenAll(requests);

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Equal(5, (await LoadUser(employee)).AccessFailedCount);
        Assert.True((await LoadUser(employee)).LockoutEnd > factory.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Enrol_is_refused_on_a_revoked_session()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.PlainClient();
        using var signIn = await factory.SignIn(client, employee);
        var token = SessionCookieValue(signIn);
        await RevokeLatestSession(employee);

        using var response = await factory.Send(client, HttpMethod.Post, "/api/v1/auth/second-factor/enrol", sessionToken: token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, (await LoadUser(employee)).FailedSecondFactorAttempts);
    }

    [Fact]
    public async Task Enrol_is_refused_after_the_pending_window()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        using var signIn = await factory.SignIn(client, employee);
        factory.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        using var response = await factory.Send(client, HttpMethod.Post, "/api/v1/auth/second-factor/enrol");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotNull((await LatestSession(employee)).RevokedAt);
    }

    [Fact]
    public async Task Password_reset_replaces_an_unconfirmed_enrolment_secret()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var first = factory.Client;
        using var signIn = await factory.SignIn(first, employee);
        using var firstEnrol = await factory.Send(first, HttpMethod.Post, "/api/v1/auth/second-factor/enrol");
        var firstSecret = JsonDocument.Parse(await BodyOf(firstEnrol)).RootElement.GetProperty("secret").GetString();
        var resetToken = await ResetToken(employee);
        using var reset = await Reset(resetToken, "New Correct Password!");
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        using var second = factory.Client;
        using var secondSignIn = await factory.SignIn(second, employee, "New Correct Password!");
        using var secondEnrol = await factory.Send(second, HttpMethod.Post, "/api/v1/auth/second-factor/enrol");
        var secondSecret = JsonDocument.Parse(await BodyOf(secondEnrol)).RootElement.GetProperty("secret").GetString();

        Assert.NotEqual(firstSecret, secondSecret);
    }

    [Fact]
    public async Task Step_up_locks_after_five_wrong_codes()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest("000000"));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.NotNull((await LatestSession(employee)).RevokedAt);
        Assert.Equal(5, (await LoadUser(employee)).FailedSecondFactorAttempts);
        using var signIn = await factory.SignIn(factory.Client, employee);
        Assert.Equal(HttpStatusCode.Locked, signIn.StatusCode);
    }

    [Fact]
    public async Task Dead_cookie_is_refused_on_route_variants()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.PlainClient();
        using var signIn = await factory.SignIn(client, employee);
        var token = SessionCookieValue(signIn);
        using var privacy = await factory.Send(client, HttpMethod.Post, PrivacyPath, new AuthEndpoints.PrivacyRequest("placeholder-v1"), sessionToken: token);
        token = SessionCookieValue(privacy);
        await RevokeLatestSession(employee);
        var before = await LatestSession(employee);

        using var stepUp = await factory.Send(client, HttpMethod.Post, "/api/v1/auth/step-up/", new AuthEndpoints.SecondFactorRequest("000000"), sessionToken: token);
        using var privacyVariant = await factory.Send(client, HttpMethod.Post, "/API/V1/AUTH/PRIVACY-NOTICE", new AuthEndpoints.PrivacyRequest("placeholder-v1"), sessionToken: token);
        var after = await LatestSession(employee);

        Assert.Equal(HttpStatusCode.Unauthorized, stepUp.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(stepUp)));
        Assert.Equal(HttpStatusCode.Unauthorized, privacyVariant.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(privacyVariant)));
        Assert.Equal(before.RevokedAt, after.RevokedAt);
        Assert.Equal(before.FailedSecondFactorAttempts, after.FailedSecondFactorAttempts);
    }

    [Theory]
    [InlineData("idle")]
    [InlineData("absolute lifetime")]
    [InlineData("pending expiry")]
    [InlineData("suspended employee")]
    [InlineData("past end date")]
    public async Task Expired_session_clears_the_cookie(string expirationCase)
    {
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero));
        var employee = await factory.CreateEmployee(expirationCase == "pending expiry" ? ["hr"] : ["dispatcher"],
            endDate: expirationCase == "past end date" ? new DateOnly(2026, 11, 6) : null);
        using var client = factory.Client;
        using var signIn = await factory.SignIn(client, employee);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);

        switch (expirationCase)
        {
            case "idle":
                await CompleteSignIn(client, employee);
                factory.Clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));
                break;
            case "absolute lifetime":
                await CompleteSignIn(client, employee);
                for (var step = 0; step < 28; step++)
                {
                    factory.Clock.Advance(TimeSpan.FromMinutes(25));
                    using var keepAlive = await factory.Send(client, HttpMethod.Get, SessionPath);
                    Assert.Equal(HttpStatusCode.OK, keepAlive.StatusCode);
                }

                factory.Clock.Advance(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(1));
                break;
            case "pending expiry":
                factory.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
                break;
            case "suspended employee":
                await CompleteSignIn(client, employee);
                await Mutate(async db =>
                {
                    var record = await db.Employees.FindAsync(employee.EmployeeId);
                    record!.Status = EmployeeStatus.Suspended;
                    await db.SaveChangesAsync();
                });
                break;
            case "past end date":
                await CompleteSignIn(client, employee);
                factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 6, 23, 1, 0, TimeSpan.Zero));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(expirationCase));
        }

        using var response = await factory.Send(client, HttpMethod.Get, SessionPath);
        var cookie = SessionCookies(response).Single();
        var expiryText = cookie.Split(';', StringSplitOptions.TrimEntries)
            .Single(part => part.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))[8..];

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(response)));
        Assert.True(DateTimeOffset.TryParse(expiryText, out var expiry));
        Assert.True(expiry < factory.Clock.GetUtcNow());
    }

    [Fact]
    public async Task Dead_cookie_does_not_block_auth_routes_in_any_case()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.PlainClient();
        using var signIn = await factory.SignIn(client, employee);
        var token = SessionCookieValue(signIn);
        await RevokeLatestSession(employee);
        var credentials = new AuthEndpoints.SignInRequest(employee.Email!, AuthApiFactory.Password);

        using var signInWithDeadCookie = await factory.Send(client, HttpMethod.Post, "/API/V1/AUTH/SIGN-IN", credentials, sessionToken: token);
        using var forgotWithDeadCookie = await factory.Send(client, HttpMethod.Post, "/API/V1/AUTH/FORGOT-PASSWORD", new AuthEndpoints.ForgotRequest(employee.Email!), sessionToken: token);
        using var signOutWithDeadCookie = await factory.Send(client, HttpMethod.Post, "/API/V1/AUTH/SIGN-OUT", sessionToken: token);

        Assert.Equal(HttpStatusCode.OK, signInWithDeadCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, forgotWithDeadCookie.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signOutWithDeadCookie.StatusCode);
    }

    [Fact]
    public async Task Healthz_ignores_a_dead_cookie()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.PlainClient();
        using var signIn = await factory.SignIn(client, employee);
        var token = SessionCookieValue(signIn);
        await RevokeLatestSession(employee);

        using var response = await factory.Send(client, HttpMethod.Get, "/healthz", sessionToken: token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"api\":\"ok\"", await BodyOf(response));
    }

    [Fact]
    public async Task Lock_clears_fully_after_it_expires()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        await FailFiveTimes(employee);
        factory.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        using var wrong = await factory.SignIn(factory.Client, employee, "wrong");
        using var right = await factory.SignIn(factory.Client, employee);

        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Equal(0, (await LoadUser(employee)).AccessFailedCount);
        Assert.Null((await LoadUser(employee)).LockoutEnd);
    }

    [Fact]
    public async Task Promotion_in_an_older_session_can_finish_enrolment()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        factory.Clock.Advance(TimeSpan.FromMinutes(11));
        await SetRoles(employee, "owner");
        await EnableAuthenticator(employee);

        using var promoted = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.Unauthorized, promoted.StatusCode);
        var session = await LatestSession(employee);
        Assert.Equal(SessionStage.PendingSecondFactor, session.Stage);
        factory.Clock.Advance(TimeSpan.FromMinutes(9));
        using var factor = await factory.Send(client, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(await factory.CurrentTotp(employee)));

        Assert.Equal(HttpStatusCode.OK, factor.StatusCode);
    }

    [Fact]
    public async Task Control_characters_in_an_email_return_400()
    {
        using var response = await factory.Send(factory.Client, HttpMethod.Post, ForgotPath, new AuthEndpoints.ForgotRequest("person\0@example.test"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("validation_error", CodeOf(await BodyOf(response)));
        Assert.Contains("email", await BodyOf(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Openapi_is_not_served_outside_development()
    {
        await using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient(OptionsFor(factory.Origin));

        using var response = await client.GetAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ------------------------------------------------------------------ lockout enumeration

    [Fact]
    public async Task Locked_account_with_a_wrong_password_looks_like_wrong_details()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        await FailFiveTimes(employee);

        using var lockedWrongPassword = await factory.SignIn(factory.Client, employee, "wrong");
        using var unknownEmail = await SignInWithEmail("unknown@example.test", "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, lockedWrongPassword.StatusCode);
        Assert.Equal(unknownEmail.StatusCode, lockedWrongPassword.StatusCode);
        Assert.Equal(await BodyOf(unknownEmail), await BodyOf(lockedWrongPassword));
        Assert.Equal("sign_in_failed", CodeOf(await BodyOf(lockedWrongPassword)));
        Assert.False(lockedWrongPassword.Headers.Contains("Set-Cookie"));

        factory.Clock.Advance(TimeSpan.FromMinutes(14));
        using var laterWrongPassword = await factory.SignIn(factory.Client, employee, "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, laterWrongPassword.StatusCode);
        factory.Clock.Advance(TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(5));
        using var afterOriginalLockout = await factory.SignIn(factory.Client, employee);
        Assert.Equal(HttpStatusCode.OK, afterOriginalLockout.StatusCode);
    }

    [Fact]
    public async Task Locked_account_with_the_right_password_gets_423()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        await FailFiveTimes(employee);

        using var firstAttempt = await factory.SignIn(factory.Client, employee);
        using var secondAttempt = await factory.SignIn(factory.Client, employee);

        Assert.Equal(HttpStatusCode.Locked, firstAttempt.StatusCode);
        Assert.Equal("account_locked", CodeOf(await BodyOf(firstAttempt)));
        Assert.False(firstAttempt.Headers.Contains("Set-Cookie"));
        Assert.Equal(HttpStatusCode.Locked, secondAttempt.StatusCode);
        Assert.Equal("account_locked", CodeOf(await BodyOf(secondAttempt)));
        Assert.Equal(0, await Db(db => db.OfficeSessions.CountAsync(row => row.UserId == employee.Id)));
    }

    // ------------------------------------------------------------------ input handling and storage

    [Fact]
    public async Task Null_fields_return_400()
    {
        using var client = factory.Client;
        var hr = await factory.CreateEmployee(["hr"]);
        using var hrClient = factory.Client;
        using var hrSignIn = await factory.SignIn(hrClient, hr);

        using var nullEmail = await factory.Send(client, HttpMethod.Post, SignInPath, new { email = (string?)null, password = "x" });
        using var nullPassword = await factory.Send(client, HttpMethod.Post, SignInPath, new { email = "a@example.test", password = (string?)null });
        using var emptyObject = await factory.Send(client, HttpMethod.Post, SignInPath, new { });
        using var nullForgotEmail = await factory.Send(client, HttpMethod.Post, ForgotPath, new { email = (string?)null });
        using var nullResetToken = await factory.Send(client, HttpMethod.Post, ResetPath, new { token = (string?)null, password = "A Long Enough Passphrase" });
        using var nullResetPassword = await factory.Send(client, HttpMethod.Post, ResetPath, new { token = new string('a', 43), password = (string?)null });
        using var nullCode = await factory.Send(hrClient, HttpMethod.Post, SecondFactorPath, new { code = (string?)null });

        var notBadRequest = new Dictionary<string, HttpStatusCode>();
        var cases = new (string Name, HttpResponseMessage Response)[]
        {
            ("sign-in null email", nullEmail),
            ("sign-in null password", nullPassword),
            ("sign-in empty object", emptyObject),
            ("forgot-password null email", nullForgotEmail),
            ("reset-password null token", nullResetToken),
            ("reset-password null password", nullResetPassword),
            ("second-factor null code", nullCode),
        };
        foreach (var (name, response) in cases.Where(entry => entry.Response.StatusCode != HttpStatusCode.BadRequest))
        {
            notBadRequest[name] = response.StatusCode;
        }

        Assert.Empty(notBadRequest);
    }

    [Fact]
    public async Task Duplicate_email_is_refused_by_the_database()
    {
        var existing = await factory.CreateEmployee(["dispatcher"]);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>();
        db.Users.Add(new OfficeUser
        {
            Id = Guid.CreateVersion7(),
            UserName = $"other-{Guid.NewGuid():N}",
            Email = existing.Email,
            NormalizedEmail = existing.Email!.ToUpperInvariant(),
            EmployeeId = existing.EmployeeId,
        });

        var exception = await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));

        var postgres = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal("23505", postgres.SqlState);
        Assert.Contains("email", postgres.ConstraintName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mailbox_is_unavailable_in_production_environment()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        await using var production = factory.WithWebHostBuilder(builder => builder.UseEnvironment("Production"));
        using var client = production.CreateClient(OptionsFor(factory.Origin));

        using var response = await factory.Send(client, HttpMethod.Post, ForgotPath, new AuthEndpoints.ForgotRequest(employee.Email!));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("reset_requested", CodeOf(await BodyOf(response)));
    }

    [Fact]
    public async Task Test_host_runs_as_the_runtime_role()
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var whoAmI = connection.CreateCommand();
        whoAmI.CommandText = "select current_user";

        var hostRole = (string?)await whoAmI.ExecuteScalarAsync(TestContext.Current.CancellationToken);

        Assert.Equal("fleetops_runtime", hostRole);
        await using var runtime = new NpgsqlConnection(factory.RuntimeConnectionString);
        await runtime.OpenAsync(TestContext.Current.CancellationToken);
        await using var owner = new NpgsqlCommand("select tableowner from pg_tables where schemaname = 'fleetops' and tablename = 'sessions'", runtime);
        Assert.Equal("fleetops_migrator", await owner.ExecuteScalarAsync(TestContext.Current.CancellationToken));
        await using var ddl = new NpgsqlCommand("create table fleetops.runtime_must_not_create (id integer)", runtime);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => ddl.ExecuteNonQueryAsync(TestContext.Current.CancellationToken));
        Assert.Equal("42501", refusal.SqlState);
    }

    // ------------------------------------------------------------------ helpers: sign-in flow

    private async Task CompleteSignIn(HttpClient client, OfficeUser employee)
    {
        using var signIn = await factory.SignIn(client, employee);
        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        await FinishSignIn(client, employee, NextOf(await BodyOf(signIn)));
    }

    private async Task FinishSignIn(HttpClient client, OfficeUser employee, string next)
    {
        for (var step = 0; step < 5 && next != "done"; step++)
        {
            switch (next)
            {
                case "enrol_second_factor":
                    using (var enrol = await factory.Send(client, HttpMethod.Post, "/api/v1/auth/second-factor/enrol"))
                    {
                        Assert.Equal(HttpStatusCode.OK, enrol.StatusCode);
                    }

                    next = await SubmitSecondFactor(client, employee);
                    break;
                case "second_factor":
                    next = await SubmitSecondFactor(client, employee);
                    break;
                case "privacy_notice":
                    using (var privacy = await factory.Send(client, HttpMethod.Post, PrivacyPath, new AuthEndpoints.PrivacyRequest("placeholder-v1")))
                    {
                        Assert.Equal(HttpStatusCode.OK, privacy.StatusCode);
                        next = NextOf(await BodyOf(privacy));
                    }

                    break;
                default:
                    throw new InvalidOperationException($"Unexpected sign-in step {next}.");
            }
        }

        Assert.Equal("done", next);
        using var session = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    }

    private async Task<string> SubmitSecondFactor(HttpClient client, OfficeUser employee)
    {
        var code = await factory.CurrentTotp(employee);
        using var response = await factory.Send(client, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(code));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return NextOf(await BodyOf(response));
    }

    private Task<HttpResponseMessage> SignInWithEmail(string email, string password) =>
        factory.Send(factory.Client, HttpMethod.Post, SignInPath, new AuthEndpoints.SignInRequest(email, password));

    private async Task FailFiveTimes(OfficeUser employee)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await factory.SignIn(factory.Client, employee, "wrong");
            Assert.Contains(failure.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Locked });
        }
    }

    private async Task<string> ResetToken(OfficeUser employee)
    {
        using var response = await factory.Send(factory.Client, HttpMethod.Post, ForgotPath, new AuthEndpoints.ForgotRequest(employee.Email!));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return factory.ReadMailbox()!.Split("token=", StringSplitOptions.None).Last();
    }

    private Task<HttpResponseMessage> Reset(string resetToken, string password) =>
        factory.Send(factory.Client, HttpMethod.Post, ResetPath, new AuthEndpoints.ResetRequest(resetToken, password));

    // ------------------------------------------------------------------ helpers: scenarios that return observations

    private sealed record SessionOutcome(
        HttpStatusCode SessionStatus,
        string SessionCode,
        DateTimeOffset? RevokedAt,
        HttpStatusCode SignInStatus,
        string SignInCode);

    private async Task<SessionOutcome> RefuseSessionAfterStatusChange(EmployeeStatus status)
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        await Mutate(async db =>
        {
            var record = await db.Employees.FindAsync(employee.EmployeeId);
            record!.Status = status;
            await db.SaveChangesAsync();
        });

        using var session = await factory.Send(client, HttpMethod.Get, SessionPath);
        var sessionBody = await BodyOf(session);
        using var signIn = await factory.SignIn(factory.Client, employee);
        var signInBody = await BodyOf(signIn);
        var revokedAt = (await LatestSession(employee)).RevokedAt;
        return new SessionOutcome(session.StatusCode, CodeOf(sessionBody), revokedAt, signIn.StatusCode, CodeOf(signInBody));
    }

    private sealed record UnsafeOutcome(
        HttpStatusCode SignInStatus,
        string SignInCode,
        HttpStatusCode SignOutStatus,
        string SignOutCode,
        DateTimeOffset? RevokedAt);

    /// <summary>Sends a sign-in and a sign-out with the given (possibly bad) origin headers.</summary>
    private async Task<UnsafeOutcome> SendUnsafeRequests(string? origin, bool includeCustomHeader)
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        var credentials = new AuthEndpoints.SignInRequest(employee.Email!, AuthApiFactory.Password);
        using var signIn = await factory.Send(
            client, HttpMethod.Post, SignInPath, credentials, includeOrigin: origin is not null, includeCustomHeader: includeCustomHeader, origin: origin);
        var signInBody = await BodyOf(signIn);

        await CompleteSignIn(client, employee);
        using var signOut = await factory.Send(
            client, HttpMethod.Post, "/api/v1/auth/sign-out", includeOrigin: origin is not null, includeCustomHeader: includeCustomHeader, origin: origin);
        var signOutBody = await BodyOf(signOut);
        var revokedAt = (await LatestSession(employee)).RevokedAt;
        return new UnsafeOutcome(signIn.StatusCode, CodeOf(signInBody), signOut.StatusCode, CodeOf(signOutBody), revokedAt);
    }

    // ------------------------------------------------------------------ helpers: database

    private Task<OfficeSession> LatestSession(OfficeUser employee) =>
        Db(db => db.OfficeSessions.Where(row => row.UserId == employee.Id).OrderByDescending(row => row.CreatedAt).FirstAsync());

    private Task<PasswordResetToken> LatestResetRow(OfficeUser employee) =>
        Db(db => db.PasswordResetTokens.Where(row => row.UserId == employee.Id).OrderByDescending(row => row.ExpiresAt).FirstAsync());

    private Task<PrivacyAcknowledgement> PrivacyRow(OfficeUser employee) =>
        Db(db => db.PrivacyAcknowledgements.SingleAsync(row => row.EmployeeId == employee.EmployeeId));

    private Task<OfficeUser> LoadUser(OfficeUser employee) =>
        Db(db => db.Users.SingleAsync(row => row.Id == employee.Id));

    private Task RevokeLatestSession(OfficeUser employee) => Mutate(async db =>
    {
        var session = await db.OfficeSessions.Where(row => row.UserId == employee.Id).OrderByDescending(row => row.CreatedAt).FirstAsync();
        session.RevokedAt = factory.Clock.GetUtcNow();
        await db.SaveChangesAsync();
    });

    private Task SetRoles(OfficeUser employee, params string[] roles) => Mutate(async db =>
    {
        var record = await db.Employees.FindAsync(employee.EmployeeId);
        record!.Roles = JsonSerializer.Serialize(roles);
        await db.SaveChangesAsync();
    });

    private async Task EnableAuthenticator(OfficeUser employee)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OfficeUser>>();
        var user = await users.FindByIdAsync(employee.Id.ToString());
        Assert.NotNull(user);
        await users.ResetAuthenticatorKeyAsync(user!);
        user!.TwoFactorEnabled = true;
        var result = await users.UpdateAsync(user);
        Assert.True(result.Succeeded, string.Join(";", result.Errors.Select(error => error.Description)));
    }

    private async Task<T> Db<T>(Func<FleetOpsDbContext, Task<T>> query)
    {
        using var scope = factory.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<FleetOpsDbContext>());
    }

    private Task Mutate(Func<FleetOpsDbContext, Task> change) => Db(async db =>
    {
        await change(db);
        return 0;
    });

    // ------------------------------------------------------------------ helpers: parsing

    private static WebApplicationFactoryClientOptions OptionsFor(string baseAddress) => new()
    {
        BaseAddress = new Uri(baseAddress),
        AllowAutoRedirect = false,
    };

    private static async Task<string> BodyOf(HttpResponseMessage response) =>
        await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

    private static string CodeOf(string body) => JsonDocument.Parse(body).RootElement.GetProperty("code").GetString()!;

    private static string NextOf(string body) => JsonDocument.Parse(body).RootElement.GetProperty("next").GetString()!;

    private static string HashOf(string rawToken) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

    private static string RandomToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static IEnumerable<string> SessionCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Where(value => value.StartsWith(AuthEndpoints.CookieName + "=", StringComparison.Ordinal))
            : [];

    /// <summary>The value of the last session cookie the response sets (the one a browser would keep).</summary>
    private static string SessionCookieValue(HttpResponseMessage response) =>
        SessionCookies(response).Last().Split(';')[0].Split('=', 2)[1];

    private static bool ClearsSessionCookie(HttpResponseMessage response) =>
        SessionCookies(response).Any(value => value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Session_token_rotates_when_second_factor_completes_sign_in()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var enrolClient = factory.Client;
        using var firstSignIn = await factory.SignIn(enrolClient, employee);
        using var enrol = await factory.Send(enrolClient, HttpMethod.Post, "/api/v1/auth/second-factor/enrol");
        using var firstFactor = await factory.Send(enrolClient, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(await factory.CurrentTotp(employee)));
        using var privacy = await factory.Send(enrolClient, HttpMethod.Post, PrivacyPath, new AuthEndpoints.PrivacyRequest("placeholder-v1"));
        Assert.Equal(HttpStatusCode.OK, privacy.StatusCode);

        using var client = factory.PlainClient();
        using var signIn = await factory.SignIn(client, employee);
        var pendingToken = SessionCookieValue(signIn);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        using var secondFactor = await factory.Send(client, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(await factory.CurrentTotp(employee)), sessionToken: pendingToken);
        var fullToken = SessionCookieValue(secondFactor);
        using var withOldToken = await factory.Send(client, HttpMethod.Get, SessionPath, sessionToken: pendingToken);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("second_factor", NextOf(await BodyOf(signIn)));
        Assert.Equal(HttpStatusCode.OK, secondFactor.StatusCode);
        Assert.Equal("done", NextOf(await BodyOf(secondFactor)));
        Assert.NotEqual(pendingToken, fullToken);
        var rotatedSession = await Db(db => db.OfficeSessions.SingleAsync(row => row.UserId == employee.Id && row.TokenHash == HashOf(fullToken)));
        Assert.Equal(HashOf(fullToken), rotatedSession.TokenHash);
        Assert.Equal(HttpStatusCode.Unauthorized, withOldToken.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(withOldToken)));
    }

    private sealed class ThrowingMailbox : IMailbox
    {
        public void Store(string address, string link) => throw new InvalidOperationException("mailbox down");
    }
}
