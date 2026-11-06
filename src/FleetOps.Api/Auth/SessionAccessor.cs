using FleetOps.Identity;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FleetOps.Api.Auth;

public sealed class SessionAccessor(FleetOpsDbContext db, ICompanyTimeService clock, TimeProvider time, IOptions<AuthOptions> options) : IAccessContextAccessor, IStepUpContextAccessor
{
    private readonly AuthOptions _options = options.Value;
    public AccessContext? Current { get; private set; }
    public bool HasFreshStepUp { get; private set; }

    public async Task LoadAsync(HttpContext httpContext)
    {
        var request = httpContext.Request;
        if (!request.Cookies.TryGetValue(AuthEndpoints.CookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var session = await db.OfficeSessions.SingleOrDefaultAsync(x => x.TokenHash == AuthTokens.Hash(raw), httpContext.RequestAborted);
        var now = time.GetUtcNow();
        if (session is null || session.RevokedAt is not null)
        {
            httpContext.Items["ProblemCode"] = "session_expired";
            return;
        }
        if (session.Stage != SessionStage.Full)
        {
            return;
        }

        var employee = await db.Employees.FindAsync([session.EmployeeId], httpContext.RequestAborted);
        var ended = employee?.EndDate is DateOnly end && clock.ToCompanyDate(now) > end;
        if (employee is null || employee.Status != EmployeeStatus.Active || ended || now - session.LastSeenAt > _options.IdleSessionLifetime || now - session.CreatedAt > _options.AbsoluteSessionLifetime)
        {
            session.RevokedAt = now;
            httpContext.Items["ProblemCode"] = "session_expired";
            await db.SaveChangesAsync(httpContext.RequestAborted);
            return;
        }

        if (now - session.LastSeenAt >= TimeSpan.FromMinutes(1))
        {
            session.LastSeenAt = now;
            await db.SaveChangesAsync(httpContext.RequestAborted);
        }

        var roles = AuthTokens.ParseRoles(employee.Roles);
        if (roles.Count == 0)
        {
            session.RevokedAt = now;
            await db.SaveChangesAsync(httpContext.RequestAborted);
            return;
        }

        Current = new AccessContext(employee.Id, roles);
        HasFreshStepUp = session.StepUpAt is DateTimeOffset stepUp && now - stepUp <= _options.StepUpLifetime;
    }
}
