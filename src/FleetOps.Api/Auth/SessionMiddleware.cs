namespace FleetOps.Api.Auth;

public sealed class SessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SessionAccessor accessor)
    {
        if (context.Request.Path.Equals("/healthz", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }
        await accessor.LoadAsync(context);
        var normalizedPath = context.Request.Path.Value?.TrimEnd('/') ?? "";
        var allowedDeadCookiePath = normalizedPath.Equals("/api/v1/auth/sign-in", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("/api/v1/auth/driver/enrol", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("/api/v1/auth/forgot-password", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("/api/v1/auth/reset-password", StringComparison.OrdinalIgnoreCase)
            || normalizedPath.Equals("/api/v1/auth/sign-out", StringComparison.OrdinalIgnoreCase);
        if (context.Items.TryGetValue("ProblemCode", out var problemCode) && problemCode is "session_expired" or "account_suspended" or "session_revoked"
            && !allowedDeadCookiePath)
        {
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = problemCode }).ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
