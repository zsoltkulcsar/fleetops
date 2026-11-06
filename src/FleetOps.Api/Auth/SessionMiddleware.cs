namespace FleetOps.Api.Auth;

public sealed class SessionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, SessionAccessor accessor)
    {
        await accessor.LoadAsync(context);
        if (context.Items.TryGetValue("ProblemCode", out var problemCode) && problemCode is "session_expired"
            && context.Request.Path.StartsWithSegments("/api/v1"))
        {
            await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                extensions: new Dictionary<string, object?> { ["code"] = problemCode }).ExecuteAsync(context);
            return;
        }
        await next(context);
    }
}
