using FleetOps.Identity;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FleetOps.Api.Auth;

public static class AuthEndpoints
{
    public const string CookieName = "__Host-fleetops_session";
    private static readonly string DummyPasswordHash = new PasswordHasher<OfficeUser>().HashPassword(new OfficeUser(), "dummy-password");

    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/auth");
        group.MapPost("/sign-in", SignIn);
        group.MapPost("/second-factor", SecondFactor);
        group.MapPost("/second-factor/enrol", Enrol);
        group.MapPost("/privacy-notice", Privacy);
        group.MapPost("/forgot-password", Forgot);
        group.MapPost("/reset-password", Reset);
        group.MapGet("/session", ([FromServices] IAccessContextAccessor accessor) => accessor.Current is null
            ? Results.Unauthorized()
            : Results.Ok(new
            {
                employeeId = accessor.Current.EmployeeId,
                roles = accessor.Current.Roles.Select(role => role.Code()).ToArray()
            }));
        group.MapPost("/sign-out", SignOut);
        group.MapPost("/step-up", StepUp);
    }

    private static async Task<IResult> SignIn(
        [FromBody] SignInRequest input,
        [FromServices] FleetOpsDbContext db,
        [FromServices] UserManager<OfficeUser> users,
        [FromServices] IHttpContextAccessor http,
        [FromServices] TimeProvider time,
        [FromServices] IOptions<AuthOptions> authOptions,
        [FromServices] ICompanyTimeService companyTime,
        [FromServices] IHostEnvironment environment)
    {
        var invalidFields = Validate(("email", input?.Email), ("password", input?.Password));
        if (invalidFields is not null) return invalidFields;
        var normalized = input.Email.Trim().ToUpperInvariant();
        var user = await users.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized);
        var locked = user is not null && IsLockedOut(user, time);

        var valid = user is not null && user.PasswordHash is not null
            ? await users.CheckPasswordAsync(user, input.Password)
            : DummyPasswordVerification(input.Password);
        if (!valid)
        {
            if (user is not null && !locked)
            {
                await IncrementAccountFailure(db, user.Id, "access_failed_count", authOptions.Value.SecondFactorFailureLimit, authOptions.Value.LockoutLifetime, time.GetUtcNow(), http.HttpContext!.RequestAborted);
            }
            return Results.Json(new { code = "sign_in_failed" }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (locked) return Results.Json(new { code = "account_locked" }, statusCode: StatusCodes.Status423Locked);

        var employee = await db.Employees.FindAsync([user!.EmployeeId], http.HttpContext?.RequestAborted ?? CancellationToken.None);
        var ended = employee?.EndDate is DateOnly endDate && companyTime.ToCompanyDate(time.GetUtcNow()) > endDate;
        if (employee is null || employee.Status != EmployeeStatus.Active || ended)
        {
            return Results.Json(new { code = "sign_in_failed" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        await users.ResetAccessFailedCountAsync(user);

        var roles = AuthTokens.ParseRoles(employee.Roles);
        var requiresFactor = SecondFactorPolicy.IsRequired(roles, authOptions.Value, environment);
        var authenticatorKey = await users.GetAuthenticatorKeyAsync(user);
        if (requiresFactor && (!user.TwoFactorEnabled || string.IsNullOrWhiteSpace(authenticatorKey)))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            authenticatorKey = await users.GetAuthenticatorKeyAsync(user);
        }

        var enrolled = user.TwoFactorEnabled && !string.IsNullOrWhiteSpace(authenticatorKey);
        var stage = requiresFactor
            ? enrolled ? SessionStage.PendingSecondFactor : SessionStage.PendingEnrolment
            : await db.PrivacyAcknowledgements.AnyAsync(x => x.EmployeeId == employee.Id && x.Version == AuthOptions.PrivacyVersion) ? SessionStage.Full : SessionStage.PendingPrivacy;
        var now = time.GetUtcNow();
        var raw = AuthTokens.New();
        db.OfficeSessions.Add(new OfficeSession
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            EmployeeId = employee.Id,
            TokenHash = AuthTokens.Hash(raw),
            Device = http.HttpContext?.Request.Headers.UserAgent.ToString() ?? "",
            Stage = stage,
            CreatedAt = now,
            StageChangedAt = now,
            LastSeenAt = now
        });
        await db.SaveChangesAsync(http.HttpContext?.RequestAborted ?? CancellationToken.None);
        SetCookie(http.HttpContext!, raw);
        return Results.Ok(new { next = Next(stage) });
    }

    private static bool DummyPasswordVerification(string password)
    {
        _ = new PasswordHasher<OfficeUser>().VerifyHashedPassword(new OfficeUser(), DummyPasswordHash, password);
        return false;
    }

    private static bool IsLockedOut(OfficeUser user, TimeProvider time) =>
        user.LockoutEnabled && user.LockoutEnd is DateTimeOffset lockoutEnd && lockoutEnd > time.GetUtcNow();

    private static async Task<IResult> SecondFactor(
        [FromBody] SecondFactorRequest input,
        [FromServices] FleetOpsDbContext db,
        [FromServices] UserManager<OfficeUser> users,
        [FromServices] IHttpContextAccessor http,
        [FromServices] TimeProvider time,
        [FromServices] IOptions<AuthOptions> options)
    {
        var invalidFields = Validate(("code", input?.Code));
        if (invalidFields is not null) return invalidFields;
        return await CompleteFactor(input, db, users, http, time, options);
    }

    private static async Task<IResult> Enrol(
        [FromServices] FleetOpsDbContext db,
        [FromServices] UserManager<OfficeUser> users,
        [FromServices] IHttpContextAccessor http,
        [FromServices] TimeProvider time,
        [FromServices] IOptions<AuthOptions> options)
    {
        var session = await FindValidSession(db, http, time, options, SessionStage.PendingEnrolment);
        var user = session is null ? null : await users.FindByIdAsync(session.UserId.ToString());
        var secret = user is null ? null : await users.GetAuthenticatorKeyAsync(user);
        if (session is null || user is null || string.IsNullOrWhiteSpace(secret))
            return Results.Unauthorized();

        var email = user.Email ?? user.UserName ?? user.Id.ToString();
        var uri = $"otpauth://totp/FleetOps:{Uri.EscapeDataString(email)}?secret={Uri.EscapeDataString(secret)}&issuer=FleetOps";
        return Results.Ok(new { secret, otpauthUri = uri });
    }

    private static async Task<IResult> CompleteFactor(SecondFactorRequest input, FleetOpsDbContext db, UserManager<OfficeUser> users, IHttpContextAccessor http, TimeProvider time, IOptions<AuthOptions> options)
    {
        var session = await FindValidSession(db, http, time, options, SessionStage.PendingSecondFactor, SessionStage.PendingEnrolment);
        var user = session is null ? null : await users.FindByIdAsync(session.UserId.ToString());
        var accepted = false;
        var authenticatorKey = user is null ? null : await users.GetAuthenticatorKeyAsync(user);
        if (session is not null && user is not null && !string.IsNullOrWhiteSpace(authenticatorKey))
        {
            var currentStep = time.GetUtcNow().ToUnixTimeSeconds() / 30;
            if (session.Stage == SessionStage.PendingEnrolment)
                user.TwoFactorEnabled = true;
            var matched = Totp.MatchedStep(authenticatorKey, input.Code, time.GetUtcNow());
            accepted = matched.HasValue && (!user.LastAcceptedTotpStep.HasValue || matched.Value > user.LastAcceptedTotpStep.Value);
            if (accepted)
            {
                user.LastAcceptedTotpStep = Totp.MatchedStep(authenticatorKey, input.Code, time.GetUtcNow());
                await ResetSessionFailures(db, session.Id, http.HttpContext!.RequestAborted);
            }
            else
            {
                var sessionFailures = await IncrementSessionFailure(db, session.Id, options.Value.SecondFactorFailureLimit, http.HttpContext!.RequestAborted);
                await IncrementAccountFailure(db, user.Id, "failed_second_factor_attempts", options.Value.SecondFactorFailureLimit, options.Value.LockoutLifetime, time.GetUtcNow(), http.HttpContext!.RequestAborted);
                if (sessionFailures >= options.Value.SecondFactorFailureLimit) await RevokeSession(db, session.Id, time.GetUtcNow(), http.HttpContext!.RequestAborted);
                if (session.Stage == SessionStage.PendingEnrolment) user.TwoFactorEnabled = false;
            }
        }
        if (!accepted)
        {
            if (session?.Stage == SessionStage.PendingEnrolment) await db.SaveChangesAsync(http.HttpContext!.RequestAborted);
            return Results.Json(new { code = "second_factor_failed" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        session!.Stage = await db.PrivacyAcknowledgements.AnyAsync(x => x.EmployeeId == session.EmployeeId && x.Version == AuthOptions.PrivacyVersion) ? SessionStage.Full : SessionStage.PendingPrivacy;
        session.StageChangedAt = time.GetUtcNow();
        session.StepUpAt = time.GetUtcNow();
        await db.SaveChangesAsync(http.HttpContext!.RequestAborted);
        if (session.Stage == SessionStage.Full)
        {
            var raw = AuthTokens.New();
            session.TokenHash = AuthTokens.Hash(raw);
            SetCookie(http.HttpContext!, raw);
            await db.SaveChangesAsync(http.HttpContext!.RequestAborted);
        }
        return Results.Ok(new { next = Next(session.Stage) });
    }

    private static async Task<IResult> Privacy(
        [FromBody] PrivacyRequest input,
        [FromServices] FleetOpsDbContext db,
        [FromServices] IHttpContextAccessor http,
        [FromServices] TimeProvider time,
        [FromServices] IOptions<AuthOptions> options)
    {
        var invalidFields = Validate(("version", input?.Version));
        if (invalidFields is not null) return invalidFields;
        var session = await FindValidSession(db, http, time, options, SessionStage.PendingPrivacy);
        if (session is null || session.Stage != SessionStage.PendingPrivacy)
        {
            return Results.Unauthorized();
        }
        db.PrivacyAcknowledgements.Add(new PrivacyAcknowledgement { Id = Guid.NewGuid(), EmployeeId = session.EmployeeId, Version = AuthOptions.PrivacyVersion, AcknowledgedAt = time.GetUtcNow() });
        session.Stage = SessionStage.Full;
        session.StageChangedAt = time.GetUtcNow();
        await db.SaveChangesAsync(http.HttpContext!.RequestAborted);
        var raw = AuthTokens.New();
        session.TokenHash = AuthTokens.Hash(raw);
        var maxAge = session.Device == "driver"
            ? TimeSpan.FromDays(await EnrolmentRules.PositiveSetting(http.HttpContext!.RequestServices.GetRequiredService<FleetOps.Modules.Setup.ISettings>(), "driver.session_days", null, http.HttpContext.RequestAborted))
            : (TimeSpan?)null;
        SetCookie(http.HttpContext!, raw, maxAge);
        await db.SaveChangesAsync(http.HttpContext!.RequestAborted);
        return Results.Ok(new { next = Next(session.Stage) });
    }

    private static async Task<IResult> Forgot(
        [FromBody] ForgotRequest input,
        [FromServices] FleetOpsDbContext db,
        [FromServices] IMailbox mailbox,
        [FromServices] TimeProvider time,
        [FromServices] IOptions<AuthOptions> options,
        [FromServices] ILoggerFactory loggerFactory)
    {
        var invalidFields = Validate(("email", input?.Email));
        if (invalidFields is not null) return invalidFields;
        if (input.Email.Any(char.IsControl)) return Results.Json(new { code = "validation_error", fields = new[] { "email" } }, statusCode: StatusCodes.Status400BadRequest);
        var user = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == input.Email.Trim().ToUpperInvariant());
        if (user is not null)
        {
            var raw = AuthTokens.New();
            db.PasswordResetTokens.Add(new PasswordResetToken { Id = Guid.NewGuid(), UserId = user.Id, TokenHash = AuthTokens.Hash(raw), ExpiresAt = time.GetUtcNow().Add(options.Value.ResetTokenLifetime) });
            try
            {
                mailbox.Store(user.Email ?? user.UserName ?? "", raw);
            }
            catch
            {
                loggerFactory.CreateLogger("FleetOps.Api.Auth.AuthEndpoints").LogWarning("Password reset mailbox delivery failed with code {Code}", "mailbox_unavailable");
            }
            await db.SaveChangesAsync();
        }
        return Results.Accepted(value: new { code = "reset_requested" });
    }

    private static async Task<IResult> Reset(
        [FromBody] ResetRequest input,
        [FromServices] FleetOpsDbContext db,
        [FromServices] UserManager<OfficeUser> users,
        [FromServices] TimeProvider time)
    {
        var invalidFields = Validate(("token", input?.Token), ("password", input?.Password));
        if (invalidFields is not null) return invalidFields;
        if (input.Token.Length != 43)
            return Results.Json(new { code = "reset_token_refused" }, statusCode: StatusCodes.Status401Unauthorized);
        var token = await db.PasswordResetTokens.SingleOrDefaultAsync(x => x.TokenHash == AuthTokens.Hash(input.Token));
        if (token is null || token.UsedAt is not null || token.ExpiresAt <= time.GetUtcNow())
        {
            return Results.Json(new { code = "reset_token_refused" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        var user = await users.FindByIdAsync(token.UserId.ToString());
        if (user is null)
        {
            return Results.Json(new { code = "reset_token_refused" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        if (!user.TwoFactorEnabled) await users.ResetAuthenticatorKeyAsync(user);
        var passwordErrors = new List<IdentityError>();
        foreach (var validator in users.PasswordValidators)
            passwordErrors.AddRange((await validator.ValidateAsync(users, user, input.Password)).Errors);
        if (passwordErrors.Count > 0) return Results.Json(new { code = "reset_password_refused" }, statusCode: StatusCodes.Status400BadRequest);
        user.PasswordHash = users.PasswordHasher.HashPassword(user, input.Password);
        var add = await users.UpdateAsync(user);
        if (!add.Succeeded)
        {
            return Results.Json(new { code = "reset_password_refused" }, statusCode: StatusCodes.Status400BadRequest);
        }
        foreach (var resetToken in await db.PasswordResetTokens.Where(x => x.UserId == user.Id && x.UsedAt == null).ToListAsync()) resetToken.UsedAt = time.GetUtcNow();
        var sessions = await db.OfficeSessions.Where(x => x.UserId == user.Id && x.RevokedAt == null).ToListAsync();
        foreach (var session in sessions)
        {
            session.RevokedAt = time.GetUtcNow();
        }
        await db.SaveChangesAsync();
        return Results.Ok(new { code = "password_reset" });
    }

    private static async Task<IResult> SignOut(
        [FromServices] FleetOpsDbContext db,
        [FromServices] IHttpContextAccessor http,
        [FromServices] TimeProvider time,
        [FromServices] IOptions<AuthOptions> options)
    {
        var session = await FindValidSession(db, http, time, options, SessionStage.Full, SessionStage.PendingSecondFactor, SessionStage.PendingEnrolment, SessionStage.PendingPrivacy);
        if (session is not null)
        {
            session.RevokedAt = time.GetUtcNow();
            await db.SaveChangesAsync();
        }
        http.HttpContext!.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict });
        return Results.NoContent();
    }

    private static async Task<IResult> StepUp(
        [FromBody] SecondFactorRequest input,
        [FromServices] FleetOpsDbContext db,
        [FromServices] UserManager<OfficeUser> users,
        [FromServices] IHttpContextAccessor http,
        [FromServices] TimeProvider time,
        [FromServices] IOptions<AuthOptions> options)
    {
        var invalidFields = Validate(("code", input?.Code));
        if (invalidFields is not null) return invalidFields;
        var session = await FindValidSession(db, http, time, options, SessionStage.Full);
        var user = session is null ? null : await users.FindByIdAsync(session.UserId.ToString());
        var accepted = session is not null && session.Stage == SessionStage.Full && user is not null && !IsLockedOut(user, time);
        var matched = user is null ? null : Totp.MatchedStep(await users.GetAuthenticatorKeyAsync(user) ?? "", input.Code, time.GetUtcNow());
        accepted = accepted && matched.HasValue && (!user!.LastAcceptedTotpStep.HasValue || user.LastAcceptedTotpStep.Value < matched.Value);
        if (!accepted)
        {
            if (session is not null && user is not null)
            {
                await IncrementSessionFailure(db, session.Id, options.Value.SecondFactorFailureLimit, http.HttpContext!.RequestAborted);
                await IncrementAccountFailure(db, user.Id, "failed_second_factor_attempts", options.Value.SecondFactorFailureLimit, options.Value.LockoutLifetime, time.GetUtcNow(), http.HttpContext!.RequestAborted);
                var failures = await ReadSessionFailures(db, session.Id, http.HttpContext!.RequestAborted);
                if (failures >= options.Value.SecondFactorFailureLimit) await RevokeSession(db, session.Id, time.GetUtcNow(), http.HttpContext!.RequestAborted);
            }
            return Results.Json(new { code = "second_factor_failed" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        user!.LastAcceptedTotpStep = matched;
        session!.StepUpAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    private static async Task<OfficeSession?> FindValidSession(FleetOpsDbContext db, IHttpContextAccessor http, TimeProvider time, IOptions<AuthOptions> options, params SessionStage[] stages)
    {
        if (http.HttpContext?.Request.Cookies.TryGetValue(CookieName, out var raw) != true) return null;
        var session = await db.OfficeSessions.SingleOrDefaultAsync(x => x.TokenHash == AuthTokens.Hash(raw!));
        if (session is null || session.RevokedAt is not null) return null;
        var now = time.GetUtcNow();
        var expired = session.Device == "driver"
            ? now - session.CreatedAt > TimeSpan.FromDays(await EnrolmentRules.PositiveSetting(http.HttpContext!.RequestServices.GetRequiredService<FleetOps.Modules.Setup.ISettings>(), "driver.session_days", null, http.HttpContext.RequestAborted))
            : now - session.LastSeenAt > options.Value.IdleSessionLifetime || now - session.CreatedAt > options.Value.AbsoluteSessionLifetime;
        if (expired) return null;
        var employee = await db.Employees.FindAsync([session.EmployeeId], http.HttpContext!.RequestAborted);
        var clock = http.HttpContext.RequestServices.GetRequiredService<ICompanyTimeService>();
        if (employee is null || employee.Status != EmployeeStatus.Active || employee.EndDate is DateOnly end && clock.ToCompanyDate(now) > end) return null;
        if (session.Stage != SessionStage.Full && now - session.StageChangedAt > options.Value.PendingSessionLifetime) return null;
        return stages.Length == 0 || stages.Contains(session.Stage) ? session : null;
    }

    private static async Task<int> IncrementSessionFailure(FleetOpsDbContext db, Guid id, int limit, CancellationToken cancellationToken)
    {
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "UPDATE fleetops.sessions SET failed_second_factor_attempts = LEAST(failed_second_factor_attempts + 1, @limit) WHERE id = @id RETURNING failed_second_factor_attempts";
        command.Parameters.Add(new NpgsqlParameter("limit", limit)); command.Parameters.Add(new NpgsqlParameter("id", id));
        if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync(cancellationToken);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<int> ReadSessionFailures(FleetOpsDbContext db, Guid id, CancellationToken cancellationToken) => await db.OfficeSessions.Where(x => x.Id == id).Select(x => x.FailedSecondFactorAttempts).SingleAsync(cancellationToken);
    private static async Task ResetSessionFailures(FleetOpsDbContext db, Guid id, CancellationToken cancellationToken) { var affected = await db.OfficeSessions.Where(x => x.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FailedSecondFactorAttempts, 0), cancellationToken); if (affected != 1) throw new InvalidOperationException("session_failure_reset_failed"); }
    private static async Task RevokeSession(FleetOpsDbContext db, Guid id, DateTimeOffset now, CancellationToken cancellationToken) { var affected = await db.OfficeSessions.Where(x => x.Id == id && x.RevokedAt == null).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.RevokedAt, now), cancellationToken); if (affected != 1) throw new InvalidOperationException("session_revoke_failed"); }
    private static async Task IncrementAccountFailure(FleetOpsDbContext db, Guid id, string column, int limit, TimeSpan lifetime, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (column is not ("access_failed_count" or "failed_second_factor_attempts")) throw new ArgumentOutOfRangeException(nameof(column));
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"UPDATE fleetops.users SET {column} = CASE WHEN lockout_end IS NOT NULL AND lockout_end <= @now THEN 1 ELSE LEAST({column} + 1, @limit) END, lockout_end = CASE WHEN (CASE WHEN lockout_end IS NOT NULL AND lockout_end <= @now THEN 1 ELSE {column} + 1 END) >= @limit THEN @until ELSE CASE WHEN lockout_end IS NOT NULL AND lockout_end <= @now THEN NULL ELSE lockout_end END END WHERE id = @id RETURNING {column}";
        command.Parameters.Add(new NpgsqlParameter("now", now)); command.Parameters.Add(new NpgsqlParameter("until", now.Add(lifetime))); command.Parameters.Add(new NpgsqlParameter("limit", limit)); command.Parameters.Add(new NpgsqlParameter("id", id));
        if (command.Connection!.State != System.Data.ConnectionState.Open) await command.Connection.OpenAsync(cancellationToken);
        if (await command.ExecuteScalarAsync(cancellationToken) is null) throw new InvalidOperationException("account_failure_update_failed");
    }

    private static IResult? Validate(params (string Name, string? Value)[] fields)
    {
        var missing = fields.Where(field => string.IsNullOrWhiteSpace(field.Value)).Select(field => field.Name).ToArray();
        return missing.Length == 0 ? null : Results.Json(new { code = "validation_error", fields = missing }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static string Next(SessionStage stage) => stage switch
    {
        SessionStage.PendingSecondFactor => "second_factor",
        SessionStage.PendingEnrolment => "enrol_second_factor",
        SessionStage.PendingPrivacy => "privacy_notice",
        SessionStage.Full => "done",
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
    internal static void SetCookie(HttpContext http, string raw, TimeSpan? maxAge = null) => http.Response.Cookies.Append(CookieName, raw, new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, MaxAge = maxAge });
    public sealed record SignInRequest(string Email, string Password);
    public sealed record SecondFactorRequest(string Code);
    public sealed record PrivacyRequest(string Version);
    public sealed record ForgotRequest(string Email);
    public sealed record ResetRequest(string Token, string Password);
}
