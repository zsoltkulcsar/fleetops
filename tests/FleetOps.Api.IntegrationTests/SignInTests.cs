using System.Net;
using System.Text.Json;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.People;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace FleetOps.Tests;

public sealed partial class SignInTests(AuthApiFactory fixture) : IClassFixture<AuthApiFactory>
{
    private const string SessionPath = "/api/v1/auth/session";
    private const string SignInPath = "/api/v1/auth/sign-in";
    private const string SecondFactorPath = "/api/v1/auth/second-factor";
    private const string PrivacyPath = "/api/v1/auth/privacy-notice";
    private const string StepUpPath = "/api/v1/auth/step-up";
    private const string ForgotPath = "/api/v1/auth/forgot-password";
    private const string ResetPath = "/api/v1/auth/reset-password";
    private const string StepUpProbePath = "/__tests/step-up-command";

    private readonly AuthApiFactory factory = fixture;

    // ------------------------------------------------------------------ cookie and password basics

    [Fact]
    public async Task Sign_in_sets_a_session_cookie_with_all_required_attributes()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);

        using var response = await factory.SignIn(factory.Client, employee);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = response.Headers.GetValues("Set-Cookie").Single();
        Assert.StartsWith("__Host-", cookie);
        Assert.Contains("HttpOnly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Domain=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_token_is_stored_only_as_a_hash()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);

        using var response = await factory.SignIn(factory.Client, employee);

        var rawToken = SessionCookieValue(response);
        var session = await LatestSession(employee);
        var expectedHash = HashOf(rawToken);
        Assert.NotEqual(rawToken, session.TokenHash);
        Assert.Equal(expectedHash, session.TokenHash);
        Assert.DoesNotContain(rawToken, session.Device, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_email_give_identical_responses()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);

        using var wrongPassword = await factory.SignIn(factory.Client, employee, "wrong");
        using var unknownEmail = await SignInWithEmail("unknown@example.test", "wrong");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(wrongPassword.StatusCode, unknownEmail.StatusCode);
        Assert.Equal(await BodyOf(wrongPassword), await BodyOf(unknownEmail));
    }

    [Fact]
    public async Task Five_failures_lock_the_account()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failure = await factory.SignIn(factory.Client, employee, "wrong");
            Assert.Contains(failure.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Locked });
        }

        using var rightPassword = await factory.SignIn(factory.Client, employee);
        Assert.Equal(HttpStatusCode.Locked, rightPassword.StatusCode);
        Assert.Equal("account_locked", CodeOf(await BodyOf(rightPassword)));
    }

    [Fact]
    public async Task Lockout_clears_after_the_lockout_time()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        await FailFiveTimes(employee);

        factory.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        using var afterLockout = await factory.SignIn(factory.Client, employee);
        Assert.Equal(HttpStatusCode.OK, afterLockout.StatusCode);
    }

    [Fact]
    public async Task Twelve_character_passphrase_is_accepted_and_shorter_is_refused()
    {
        var acceptedUser = await factory.CreateEmployee(["dispatcher"]);
        var acceptedToken = await ResetToken(acceptedUser);
        using var acceptedResult = await Reset(acceptedToken, "twelve chars");
        Assert.Equal(HttpStatusCode.OK, acceptedResult.StatusCode);

        var refusedUser = await factory.CreateEmployee(["dispatcher"]);
        var refusedToken = await ResetToken(refusedUser);
        using var refusedResult = await Reset(refusedToken, "short");

        Assert.Equal(HttpStatusCode.BadRequest, refusedResult.StatusCode);
        Assert.Equal("reset_password_refused", CodeOf(await BodyOf(refusedResult)));
    }

    // ------------------------------------------------------------------ second factor

    [Fact]
    public async Task Hr_needs_a_second_factor_before_any_access()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;

        using var signIn = await factory.SignIn(client, employee);
        using var sessionBefore = await factory.Send(client, HttpMethod.Get, SessionPath);
        using var vehicleBefore = await factory.Send(client, HttpMethod.Get, $"/api/v1/fleet/vehicles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("enrol_second_factor", NextOf(await BodyOf(signIn)));
        Assert.Equal(HttpStatusCode.Unauthorized, sessionBefore.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, vehicleBefore.StatusCode);

        await FinishSignIn(client, employee, "enrol_second_factor");
        using var sessionAfter = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.OK, sessionAfter.StatusCode);
        Assert.Contains("hr", await BodyOf(sessionAfter), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Owner_needs_a_second_factor_before_any_access()
    {
        var employee = await factory.CreateEmployee(["owner"]);
        using var client = factory.Client;

        using var signIn = await factory.SignIn(client, employee);
        using var sessionBefore = await factory.Send(client, HttpMethod.Get, SessionPath);
        using var vehicleBefore = await factory.Send(client, HttpMethod.Get, $"/api/v1/fleet/vehicles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("enrol_second_factor", NextOf(await BodyOf(signIn)));
        Assert.Equal(HttpStatusCode.Unauthorized, sessionBefore.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, vehicleBefore.StatusCode);

        await FinishSignIn(client, employee, "enrol_second_factor");
        using var sessionAfter = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.OK, sessionAfter.StatusCode);
        Assert.Contains("owner", await BodyOf(sessionAfter), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatcher_does_not_need_a_second_factor()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;

        await CompleteSignIn(client, employee);

        using var response = await factory.Send(client, HttpMethod.Get, SessionPath);
        var body = await BodyOf(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("second_factor", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dispatcher", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wrong_second_factor_code_is_refused()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        using var signIn = await factory.SignIn(client, employee);

        using var response = await factory.Send(client, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest("000000"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("second_factor_failed", CodeOf(await BodyOf(response)));
        using var session = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
    }

    [Fact]
    public async Task Enrolment_is_required_before_access_for_hr_without_a_factor()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;

        using var signIn = await factory.SignIn(client, employee);
        Assert.Equal("enrol_second_factor", NextOf(await BodyOf(signIn)));
        using var sessionBefore = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.Unauthorized, sessionBefore.StatusCode);

        using var enrol = await factory.Send(client, HttpMethod.Post, "/api/v1/auth/second-factor/enrol");
        Assert.Equal(HttpStatusCode.OK, enrol.StatusCode);
        Assert.Contains("secret", await BodyOf(enrol));
    }

    [Fact]
    public async Task Reused_totp_code_is_refused()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var first = factory.Client;
        using var firstSignIn = await factory.SignIn(first, employee);
        using var enrol = await factory.Send(first, HttpMethod.Post, "/api/v1/auth/second-factor/enrol");
        Assert.Equal(HttpStatusCode.OK, enrol.StatusCode);
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        var code = await factory.CurrentTotp(employee);
        using var accepted = await factory.Send(first, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(code));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        using var second = factory.Client;
        using var secondSignIn = await factory.SignIn(second, employee);
        using var replay = await factory.Send(second, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(code));

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("second_factor_failed", CodeOf(await BodyOf(replay)));
    }

    [Fact]
    public async Task Second_factor_locks_after_five_wrong_codes()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var client = factory.Client;
        using var signIn = await factory.SignIn(client, employee);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var wrong = await factory.Send(client, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest("000000"));
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        }

        Assert.NotNull((await LatestSession(employee)).RevokedAt);
        Assert.Equal(5, (await LoadUser(employee)).FailedSecondFactorAttempts);
        var correctCode = await factory.CurrentTotp(employee);
        using var afterLock = await factory.Send(client, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(correctCode));
        Assert.Equal(HttpStatusCode.Unauthorized, afterLock.StatusCode);
    }

    [Fact]
    public async Task Pending_session_expires_after_ten_minutes()
    {
        var onTime = await factory.CreateEmployee(["hr"]);
        using var onTimeClient = factory.Client;
        using var onTimeSignIn = await factory.SignIn(onTimeClient, onTime);
        factory.Clock.Advance(TimeSpan.FromMinutes(9));
        var onTimeCode = await factory.CurrentTotp(onTime);
        using var onTimeFactor = await factory.Send(onTimeClient, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(onTimeCode));
        Assert.Equal(HttpStatusCode.OK, onTimeFactor.StatusCode);

        var tooLate = await factory.CreateEmployee(["hr"]);
        using var lateClient = factory.Client;
        using var lateSignIn = await factory.SignIn(lateClient, tooLate);
        factory.Clock.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));
        var validCode = await factory.CurrentTotp(tooLate);

        using var lateFactor = await factory.Send(lateClient, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(validCode));

        Assert.Equal(HttpStatusCode.Unauthorized, lateFactor.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(lateFactor)));
        Assert.NotNull((await LatestSession(tooLate)).RevokedAt);
    }

    [Fact]
    public async Task Step_up_is_refused_without_an_authenticator()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        var codeFromAnEmptyKey = AuthApiFactory.TotpCode([], factory.Clock.GetUtcNow());

        using var response = await factory.Send(client, HttpMethod.Post, StepUpPath, new AuthEndpoints.SecondFactorRequest(codeFromAnEmptyKey));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("second_factor_failed", CodeOf(await BodyOf(response)));
        Assert.Null((await LatestSession(employee)).StepUpAt);
        using var probe = await factory.Send(client, HttpMethod.Post, StepUpProbePath);
        Assert.Equal(HttpStatusCode.Forbidden, probe.StatusCode);
    }

    [Fact]
    public async Task Totp_code_cannot_be_reused_in_the_next_time_step()
    {
        var employee = await factory.CreateEmployee(["hr"]);
        using var first = factory.Client;
        await CompleteSignIn(first, employee);
        var spentCode = await factory.CurrentTotp(employee);

        using var second = factory.Client;
        using var secondSignIn = await factory.SignIn(second, employee);
        Assert.Equal("second_factor", NextOf(await BodyOf(secondSignIn)));
        factory.Clock.Advance(TimeSpan.FromSeconds(30));
        using var replay = await factory.Send(second, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(spentCode));

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Equal("second_factor_failed", CodeOf(await BodyOf(replay)));
        var freshCode = await factory.CurrentTotp(employee);
        using var freshAttempt = await factory.Send(second, HttpMethod.Post, SecondFactorPath, new AuthEndpoints.SecondFactorRequest(freshCode));
        Assert.Equal(HttpStatusCode.OK, freshAttempt.StatusCode);
    }

    // ------------------------------------------------------------------ privacy notice

    [Fact]
    public async Task First_sign_in_requires_the_privacy_notice_once()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var first = factory.Client;
        using var signIn = await factory.SignIn(first, employee);
        Assert.Equal("privacy_notice", NextOf(await BodyOf(signIn)));

        using var acknowledgement = await factory.Send(first, HttpMethod.Post, PrivacyPath, new AuthEndpoints.PrivacyRequest("placeholder-v1"));

        Assert.Equal(HttpStatusCode.OK, acknowledgement.StatusCode);
        var row = await PrivacyRow(employee);
        Assert.Equal("placeholder-v1", row.Version);
        Assert.Equal(factory.Clock.GetUtcNow(), row.AcknowledgedAt);
        using var second = factory.Client;
        using var again = await factory.SignIn(second, employee);
        Assert.Equal("done", NextOf(await BodyOf(again)));
    }

    [Fact]
    public async Task Privacy_version_is_constant_and_not_configuration_overridable()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var original = factory.Client;
        await CompleteSignIn(original, employee);

        await Mutate(async db =>
        {
            var row = await db.PrivacyAcknowledgements.SingleAsync(row => row.EmployeeId == employee.EmployeeId);
            row.Version = "stale-version";
            await db.SaveChangesAsync();
        });

        await using var newerVersion = factory.WithWebHostBuilder(builder => builder.UseSetting("Auth:PrivacyVersion", "placeholder-v2"));
        using var client = newerVersion.CreateClient(OptionsFor(factory.Origin));
        using var signIn = await factory.SignIn(client, employee);

        Assert.Equal(HttpStatusCode.OK, signIn.StatusCode);
        Assert.Equal("privacy_notice", NextOf(await BodyOf(signIn)));
        using var acknowledgement = await factory.Send(client, HttpMethod.Post, PrivacyPath, new AuthEndpoints.PrivacyRequest("placeholder-v2"));
        Assert.Equal(HttpStatusCode.OK, acknowledgement.StatusCode);
        var versions = await Db(db => db.PrivacyAcknowledgements
            .Where(row => row.EmployeeId == employee.EmployeeId)
            .Select(row => row.Version)
            .ToListAsync());
        Assert.Contains("stale-version", versions);
        Assert.Contains(AuthOptions.PrivacyVersion, versions);
        Assert.DoesNotContain("placeholder-v2", versions);
        using var third = newerVersion.CreateClient(OptionsFor(factory.Origin));
        using var afterAcknowledging = await factory.SignIn(third, employee);
        Assert.Equal("done", NextOf(await BodyOf(afterAcknowledging)));
    }

    // ------------------------------------------------------------------ forgot and reset password

    [Fact]
    public async Task Forgot_password_response_is_identical_for_known_and_unknown_email()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);

        using var known = await factory.Send(factory.Client, HttpMethod.Post, ForgotPath, new AuthEndpoints.ForgotRequest(employee.Email!));
        using var unknown = await factory.Send(factory.Client, HttpMethod.Post, ForgotPath, new AuthEndpoints.ForgotRequest("unknown@example.test"));

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await BodyOf(known), await BodyOf(unknown));
        Assert.NotNull(factory.ReadMailbox());
    }

    [Fact]
    public async Task Forgot_password_is_identical_when_the_mailbox_fails()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var healthyKnown = await factory.Send(factory.Client, HttpMethod.Post, ForgotPath, new AuthEndpoints.ForgotRequest(employee.Email!));
        var healthyBody = await BodyOf(healthyKnown);

        await using var brokenMailbox = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMailbox>();
            services.AddSingleton<IMailbox, ThrowingMailbox>();
        }));
        using var client = brokenMailbox.CreateClient(OptionsFor(factory.Origin));
        using var known = await factory.Send(client, HttpMethod.Post, ForgotPath, new AuthEndpoints.ForgotRequest(employee.Email!));
        using var unknown = await factory.Send(client, HttpMethod.Post, ForgotPath, new AuthEndpoints.ForgotRequest("unknown@example.test"));

        Assert.Equal(HttpStatusCode.Accepted, healthyKnown.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(healthyBody, await BodyOf(known));
        Assert.Equal(healthyBody, await BodyOf(unknown));
    }

    [Fact]
    public async Task Reset_token_is_single_use()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        var resetToken = await ResetToken(employee);

        using var first = await Reset(resetToken, "New Correct Password!");
        using var second = await Reset(resetToken, "Another Password!");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal("reset_token_refused", CodeOf(await BodyOf(second)));
    }

    [Fact]
    public async Task Expired_reset_token_is_refused()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        var resetToken = await ResetToken(employee);
        factory.Clock.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(1));

        using var response = await Reset(resetToken, "New Correct Password!");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("reset_token_refused", CodeOf(await BodyOf(response)));
    }

    [Fact]
    public async Task Reset_token_is_stored_only_as_a_hash()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        var resetToken = await ResetToken(employee);

        var row = await LatestResetRow(employee);

        Assert.NotEqual(resetToken, row.TokenHash);
        Assert.Equal(HashOf(resetToken), row.TokenHash);
    }

    [Fact]
    public async Task Reset_password_revokes_existing_sessions()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        var resetToken = await ResetToken(employee);

        using var reset = await Reset(resetToken, "New Correct Password!");
        using var session = await factory.Send(client, HttpMethod.Get, SessionPath);

        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, session.StatusCode);
        Assert.NotNull((await LatestSession(employee)).RevokedAt);
    }

    [Fact]
    public async Task Refused_reset_keeps_the_old_password()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        var resetToken = await ResetToken(employee);

        using var refused = await Reset(resetToken, "short");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using var oldPassword = await factory.SignIn(factory.Client, employee);
        Assert.Equal(HttpStatusCode.OK, oldPassword.StatusCode);
        using var refusedPassword = await factory.SignIn(factory.Client, employee, "short");
        Assert.Equal(HttpStatusCode.Unauthorized, refusedPassword.StatusCode);
        using var retry = await Reset(resetToken, "A Long Enough Passphrase");
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var newPassword = await factory.SignIn(factory.Client, employee, "A Long Enough Passphrase");
        Assert.Equal(HttpStatusCode.OK, newPassword.StatusCode);
    }

    [Fact]
    public async Task Successful_reset_invalidates_older_tokens()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        var olderToken = await ResetToken(employee);
        var newerToken = await ResetToken(employee);
        Assert.NotEqual(olderToken, newerToken);

        using var success = await Reset(newerToken, "New Correct Password!");
        using var olderAttempt = await Reset(olderToken, "Another Correct Password!");

        Assert.Equal(HttpStatusCode.OK, success.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, olderAttempt.StatusCode);
        Assert.Equal("reset_token_refused", CodeOf(await BodyOf(olderAttempt)));
        var rows = await Db(db => db.PasswordResetTokens.Where(row => row.UserId == employee.Id).ToListAsync());
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.NotNull(row.UsedAt));
    }

    // ------------------------------------------------------------------ session lifetime and status

    [Fact]
    public async Task Idle_session_is_refused_and_revoked()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        factory.Clock.Advance(TimeSpan.FromMinutes(30) + TimeSpan.FromSeconds(1));

        using var response = await factory.Send(client, HttpMethod.Get, SessionPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(response)));
        Assert.NotNull((await LatestSession(employee)).RevokedAt);
    }

    [Fact]
    public async Task Session_past_its_absolute_lifetime_is_refused()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        for (var step = 0; step < 28; step++)
        {
            factory.Clock.Advance(TimeSpan.FromMinutes(25));
            using var keepAlive = await factory.Send(client, HttpMethod.Get, SessionPath);
            Assert.Equal(HttpStatusCode.OK, keepAlive.StatusCode);
        }

        factory.Clock.Advance(TimeSpan.FromMinutes(20) + TimeSpan.FromSeconds(1));

        using var response = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(response)));
    }

    [Fact]
    public async Task Session_of_an_employee_past_end_date_is_refused()
    {
        var employee = await factory.CreateEmployee(["dispatcher"], endDate: new DateOnly(2026, 11, 6));
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 6, 22, 45, 0, TimeSpan.Zero));
        using var client = factory.Client;
        await CompleteSignIn(client, employee);
        using var beforeMidnight = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.OK, beforeMidnight.StatusCode);

        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 6, 23, 1, 0, TimeSpan.Zero));

        using var response = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(response)));
        Assert.NotNull((await LatestSession(employee)).RevokedAt);
    }

    [Fact]
    public async Task Sign_in_is_refused_past_end_date()
    {
        factory.Clock.SetUtcNow(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero));
        var ended = await factory.CreateEmployee(["dispatcher"], endDate: new DateOnly(2026, 11, 5));
        var lastDay = await factory.CreateEmployee(["dispatcher"], endDate: new DateOnly(2026, 11, 6));

        using var wrongPassword = await factory.SignIn(factory.Client, ended, "wrong");
        using var endedSignIn = await factory.SignIn(factory.Client, ended);
        using var lastDaySignIn = await factory.SignIn(factory.Client, lastDay);

        Assert.Equal(HttpStatusCode.Unauthorized, endedSignIn.StatusCode);
        Assert.Equal("sign_in_failed", CodeOf(await BodyOf(endedSignIn)));
        Assert.Equal(await BodyOf(wrongPassword), await BodyOf(endedSignIn));
        Assert.False(endedSignIn.Headers.Contains("Set-Cookie"));
        var sessions = await Db(db => db.OfficeSessions.CountAsync(row => row.UserId == ended.Id));
        Assert.Equal(0, sessions);
        Assert.Equal(HttpStatusCode.OK, lastDaySignIn.StatusCode);
    }

    [Fact]
    public async Task Session_of_a_suspended_employee_is_refused()
    {
        var outcome = await RefuseSessionAfterStatusChange(EmployeeStatus.Suspended);

        Assert.Equal(HttpStatusCode.Unauthorized, outcome.SessionStatus);
        Assert.Equal("session_expired", outcome.SessionCode);
        Assert.NotNull(outcome.RevokedAt);
        Assert.Equal(HttpStatusCode.Unauthorized, outcome.SignInStatus);
        Assert.Equal("sign_in_failed", outcome.SignInCode);
    }

    [Fact]
    public async Task Session_of_a_left_employee_is_refused()
    {
        var outcome = await RefuseSessionAfterStatusChange(EmployeeStatus.Left);

        Assert.Equal(HttpStatusCode.Unauthorized, outcome.SessionStatus);
        Assert.Equal("session_expired", outcome.SessionCode);
        Assert.NotNull(outcome.RevokedAt);
        Assert.Equal(HttpStatusCode.Unauthorized, outcome.SignInStatus);
        Assert.Equal("sign_in_failed", outcome.SignInCode);
    }

    [Fact]
    public async Task Revoked_session_is_refused_on_the_next_request()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);

        await RevokeLatestSession(employee);

        using var response = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(response)));
    }

    [Fact]
    public async Task Stale_cookie_does_not_block_sign_in_and_is_cleared()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.PlainClient();
        using var signIn = await factory.SignIn(client, employee);
        var firstToken = SessionCookieValue(signIn);
        using var privacy = await factory.Send(client, HttpMethod.Post, PrivacyPath, new AuthEndpoints.PrivacyRequest("placeholder-v1"), sessionToken: firstToken);
        var liveToken = SessionCookieValue(privacy);
        using var signOut = await factory.Send(client, HttpMethod.Post, "/api/v1/auth/sign-out", sessionToken: liveToken);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
        var credentials = new AuthEndpoints.SignInRequest(employee.Email!, AuthApiFactory.Password);

        using var revoked = await factory.Send(client, HttpMethod.Get, SessionPath, sessionToken: liveToken);
        using var neverIssued = await factory.Send(client, HttpMethod.Get, SessionPath, sessionToken: RandomToken());
        using var signInWithStaleCookie = await factory.Send(client, HttpMethod.Post, SignInPath, credentials, sessionToken: liveToken);
        using var forgotWithStaleCookie = await factory.Send(
            client, HttpMethod.Post, ForgotPath, new AuthEndpoints.ForgotRequest(employee.Email!), sessionToken: liveToken);
        using var resetWithStaleCookie = await factory.Send(
            client, HttpMethod.Post, ResetPath, new AuthEndpoints.ResetRequest(new string('a', 43), "A Long Enough Passphrase"), sessionToken: liveToken);

        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        Assert.Equal("session_expired", CodeOf(await BodyOf(revoked)));
        Assert.True(ClearsSessionCookie(revoked), "A revoked cookie must be cleared.");
        Assert.Equal(HttpStatusCode.Unauthorized, neverIssued.StatusCode);
        Assert.True(ClearsSessionCookie(neverIssued), "An unknown cookie must be cleared.");
        Assert.Equal(HttpStatusCode.OK, signInWithStaleCookie.StatusCode);
        Assert.NotEqual(liveToken, SessionCookieValue(signInWithStaleCookie));
        Assert.Equal(HttpStatusCode.Accepted, forgotWithStaleCookie.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, resetWithStaleCookie.StatusCode);
        Assert.Equal("reset_token_refused", CodeOf(await BodyOf(resetWithStaleCookie)));
    }

    [Fact]
    public async Task Sign_out_revokes_the_session_and_clears_the_cookie()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.Client;
        await CompleteSignIn(client, employee);

        using var response = await factory.Send(client, HttpMethod.Post, "/api/v1/auth/sign-out");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(ClearsSessionCookie(response));
        Assert.NotNull((await LatestSession(employee)).RevokedAt);
        using var after = await factory.Send(client, HttpMethod.Get, SessionPath);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Session_token_rotates_when_sign_in_completes()
    {
        var employee = await factory.CreateEmployee(["dispatcher"]);
        using var client = factory.PlainClient();
        using var signIn = await factory.SignIn(client, employee);
        var pendingToken = SessionCookieValue(signIn);
        Assert.Equal("privacy_notice", NextOf(await BodyOf(signIn)));

        using var privacy = await factory.Send(client, HttpMethod.Post, PrivacyPath, new AuthEndpoints.PrivacyRequest("placeholder-v1"), sessionToken: pendingToken);
        var fullToken = SessionCookieValue(privacy);

        Assert.Equal(HttpStatusCode.OK, privacy.StatusCode);
        Assert.NotEqual(pendingToken, fullToken);
        Assert.Equal(HashOf(fullToken), (await LatestSession(employee)).TokenHash);
        using var withOldToken = await factory.Send(client, HttpMethod.Get, SessionPath, sessionToken: pendingToken);
        using var withNewToken = await factory.Send(client, HttpMethod.Get, SessionPath, sessionToken: fullToken);
        Assert.Equal(HttpStatusCode.Unauthorized, withOldToken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withNewToken.StatusCode);
    }
}
