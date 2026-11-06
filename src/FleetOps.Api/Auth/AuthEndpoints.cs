using FleetOps.Identity;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.People;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
        [FromServices] ICompanyTimeService companyTime)
    {
        var invalidFields = Validate(("email", input?.Email), ("password", input?.Password));
        if (invalidFields is not null) return invalidFields;
        var normalized = input.Email.Trim().ToUpperInvariant();
        var user = await users.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized);
        var locked = user is not null && user.AccessFailedCount >= authOptions.Value.SecondFactorFailureLimit && IsLockedOut(user, time);

        var valid = user is not null
            ? await users.CheckPasswordAsync(user, input.Password)
            : DummyPasswordVerification(input.Password);
        if (!valid)
        {
            if (user is not null && !locked)
            {
                user.AccessFailedCount++;
                if (user.AccessFailedCount >= authOptions.Value.SecondFactorFailureLimit)
                {
                    user.LockoutEnd = time.GetUtcNow().Add(authOptions.Value.LockoutLifetime);
                    user.AccessFailedCount = authOptions.Value.SecondFactorFailureLimit;
                    await users.UpdateAsync(user);
                }
                await users.UpdateAsync(user);
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
        var requiresFactor = roles.Contains(RoleCode.Hr) || roles.Contains(RoleCode.Owner);
        var authenticatorKey = await users.GetAuthenticatorKeyAsync(user);
        if (requiresFactor && string.IsNullOrWhiteSpace(authenticatorKey))
        {
            await users.ResetAuthenticatorKeyAsync(user);
            authenticatorKey = await users.GetAuthenticatorKeyAsync(user);
        }

        var enrolled = user.TwoFactorEnabled && !string.IsNullOrWhiteSpace(authenticatorKey);
        var stage = requiresFactor
            ? enrolled ? SessionStage.PendingSecondFactor : SessionStage.PendingEnrolment
            : await db.PrivacyAcknowledgements.AnyAsync(x => x.EmployeeId == employee.Id && x.Version == authOptions.Value.PrivacyVersion) ? SessionStage.Full : SessionStage.PendingPrivacy;
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
            LastSeenAt = now
        });
        await db.SaveChangesAsync(http.HttpContext?.RequestAborted ?? CancellationToken.None);
        SetCookie(http.HttpContext!, raw);
        return Results.Ok(new { next = Next(stage) });
    }

    private static bool DummyPasswordVerification(string password) => new PasswordHasher<OfficeUser>().VerifyHashedPassword(new OfficeUser(), DummyPasswordHash, password) != PasswordVerificationResult.Failed;

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
        [FromServices] IHttpContextAccessor http)
    {
        var session = await FindSession(db, http);
        var user = session is null ? null : await users.FindByIdAsync(session.UserId.ToString());
        var secret = user is null ? null : await users.GetAuthenticatorKeyAsync(user);
        if (session is null || user is null || session.Stage != SessionStage.PendingEnrolment || string.IsNullOrWhiteSpace(secret))
            return Results.Unauthorized();

        var email = user.Email ?? user.UserName ?? user.Id.ToString();
        var uri = $"otpauth://totp/FleetOps:{Uri.EscapeDataString(email)}?secret={Uri.EscapeDataString(secret)}&issuer=FleetOps";
        return Results.Ok(new { secret, otpauthUri = uri });
    }

    private static async Task<IResult> CompleteFactor(SecondFactorRequest input, FleetOpsDbContext db, UserManager<OfficeUser> users, IHttpContextAccessor http, TimeProvider time, IOptions<AuthOptions> options)
    {
        var session = await FindSession(db, http);
        var user = session is null ? null : await users.FindByIdAsync(session.UserId.ToString());
        var accepted = false;
        var authenticatorKey = user is null ? null : await users.GetAuthenticatorKeyAsync(user);
        if (session is not null && session.RevokedAt is null && user is not null && (session.Stage is SessionStage.PendingSecondFactor or SessionStage.PendingEnrolment) && session.FailedSecondFactorAttempts < options.Value.SecondFactorFailureLimit && !string.IsNullOrWhiteSpace(authenticatorKey))
        {
            var currentStep = time.GetUtcNow().ToUnixTimeSeconds() / 30;
            if (session.Stage == SessionStage.PendingEnrolment)
                user.TwoFactorEnabled = true;
            var matched = Totp.MatchedStep(authenticatorKey, input.Code, time.GetUtcNow());
            accepted = matched.HasValue && (!user.LastAcceptedTotpStep.HasValue || matched.Value > user.LastAcceptedTotpStep.Value);
            if (accepted)
            {
                user.LastAcceptedTotpStep = Totp.MatchedStep(authenticatorKey, input.Code, time.GetUtcNow());
                session.FailedSecondFactorAttempts = 0;
            }
            else { session.FailedSecondFactorAttempts++; if (session.FailedSecondFactorAttempts >= 5) { session.RevokedAt = time.GetUtcNow(); user.AccessFailedCount = 5; } if (session.Stage == SessionStage.PendingEnrolment) user.TwoFactorEnabled = false; }
        }
        if (!accepted)
        {
            if (session is not null) await db.SaveChangesAsync(http.HttpContext!.RequestAborted);
            return Results.Json(new { code = "second_factor_failed" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        session!.Stage = await db.PrivacyAcknowledgements.AnyAsync(x => x.EmployeeId == session.EmployeeId && x.Version == options.Value.PrivacyVersion) ? SessionStage.Full : SessionStage.PendingPrivacy;
        session.StepUpAt = time.GetUtcNow();
        await db.SaveChangesAsync(http.HttpContext!.RequestAborted);
        if (session.Stage == SessionStage.Full)
        {
            var raw = AuthTokens.New(); session.TokenHash = AuthTokens.Hash(raw); SetCookie(http.HttpContext!, raw);
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
        var session = await FindSession(db, http);
        if (session is null || session.Stage != SessionStage.PendingPrivacy)
        {
            return Results.Unauthorized();
        }
        db.PrivacyAcknowledgements.Add(new PrivacyAcknowledgement { Id = Guid.CreateVersion7(), EmployeeId = session.EmployeeId, Version = options.Value.PrivacyVersion, AcknowledgedAt = time.GetUtcNow() });
        session.Stage = SessionStage.Full;
        await db.SaveChangesAsync(http.HttpContext!.RequestAborted);
        var raw = AuthTokens.New(); session.TokenHash = AuthTokens.Hash(raw); SetCookie(http.HttpContext!, raw);
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
        [FromServices] TimeProvider time)
    {
        var session = await FindSession(db, http);
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
        [FromServices] TimeProvider time)
    {
        var invalidFields = Validate(("code", input?.Code));
        if (invalidFields is not null) return invalidFields;
        var session = await FindSession(db, http);
        var user = session is null ? null : await users.FindByIdAsync(session.UserId.ToString());
        var accepted = session is not null && session.Stage == SessionStage.Full && user is not null;
        var matched = user is null ? null : Totp.MatchedStep(await users.GetAuthenticatorKeyAsync(user) ?? "", input.Code, time.GetUtcNow());
        accepted = accepted && matched.HasValue && (!user!.LastAcceptedTotpStep.HasValue || user.LastAcceptedTotpStep.Value < matched.Value);
        if (!accepted)
        {
            return Results.Json(new { code = "second_factor_failed" }, statusCode: StatusCodes.Status401Unauthorized);
        }
        user!.LastAcceptedTotpStep = matched;
        session!.StepUpAt = time.GetUtcNow();
        await db.SaveChangesAsync();
        return Results.Ok();
    }

    private static Task<OfficeSession?> FindSession(FleetOpsDbContext db, IHttpContextAccessor http) => http.HttpContext?.Request.Cookies.TryGetValue(CookieName, out var raw) == true ? db.OfficeSessions.SingleOrDefaultAsync(x => x.TokenHash == AuthTokens.Hash(raw!)) : Task.FromResult<OfficeSession?>(null);

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
    private static void SetCookie(HttpContext http, string raw) => http.Response.Cookies.Append(CookieName, raw, new CookieOptions { Path = "/", HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict });
    public sealed record SignInRequest(string Email, string Password);
    public sealed record SecondFactorRequest(string Code);
    public sealed record PrivacyRequest(string Version);
    public sealed record ForgotRequest(string Email);
    public sealed record ResetRequest(string Token, string Password);
}
