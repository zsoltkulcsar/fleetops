namespace FleetOps.Api.Auth;

public sealed class SessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SessionAccessor accessor)
    {
        await accessor.LoadAsync(context);
        var authPath = context.Request.Path.StartsWithSegments("/api/v1/auth");
        var allowedAuth = context.Request.Path.Value is "/api/v1/auth/session" or "/api/v1/auth/second-factor" or "/api/v1/auth/privacy-notice" or "/api/v1/auth/step-up" or "/api/v1/auth/sign-out";
        if (context.Items.TryGetValue("ProblemCode", out var problemCode) && problemCode is "session_expired"
            && (!authPath || allowedAuth))
        {
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = problemCode }).ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
