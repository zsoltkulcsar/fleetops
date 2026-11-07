using System.Security.Cryptography;
using System.Text;
using FleetOps.Migrations;

namespace FleetOps.Api.Jobs;

public static class JobEndpoints
{
    public static async Task<IResult> RunAsync(string name, JobTrigger trigger, HttpContext context, JobRunner runner, CancellationToken cancellationToken)
    {
        if (!ValidSecret(context)) return Results.Json(new { code = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        if (!runner.Contains(name)) return Results.NotFound();
        context.Response.Headers.CacheControl = "no-store";
        var result = await runner.RunAsync(name, trigger, cancellationToken);
        return result.Status == "failed"
            ? Results.Json(new { code = "job_failed" }, statusCode: StatusCodes.Status500InternalServerError)
            : Results.Ok(new { status = result.Status, processed = result.Processed });
    }

    public static async Task<IResult> RunNightlyAsync(HttpContext context, JobRunner runner, CancellationToken cancellationToken)
    {
        if (!ValidSecret(context)) return Results.Json(new { code = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
        context.Response.Headers.CacheControl = "no-store";
        var result = await runner.RunNightlyAsync(JobTrigger.scheduler, cancellationToken);
        return result.Status == "failed"
            ? Results.Json(new { code = "job_failed" }, statusCode: StatusCodes.Status500InternalServerError)
            : Results.Ok(new { status = result.Status, processed = result.Processed });
    }

    private static bool ValidSecret(HttpContext context)
    {
        var secret = Environment.GetEnvironmentVariable("CRON_SECRET");
        if (string.IsNullOrWhiteSpace(secret)) secret = context.RequestServices.GetRequiredService<IConfiguration>()["Jobs:Secret"];
        var authorization = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(secret) || !authorization.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var supplied = authorization[7..].Trim();
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}
