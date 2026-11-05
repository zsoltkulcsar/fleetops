using System.Text.Json.Serialization;
using FleetOps.Kernel;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("code", context.HttpContext.Items["ProblemCode"] ?? "unexpected_error");
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<ICompanyTimeService>(_ =>
    new CompanyTimeService(CompanyTimeService.Resolve(builder.Configuration["Company:Timezone"] ?? "Europe/Berlin")));
builder.Services.AddSingleton<IDatabaseHealth, DatabaseHealth>();

var app = builder.Build();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    SuppressDiagnosticsCallback = _ => true,
    ExceptionHandler = async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        app.Logger.LogError("Unhandled exception {ExceptionType} TraceId {TraceId}",
            exception?.GetType().Name ?? "Unknown", context.TraceIdentifier);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context);
    }
});
app.MapOpenApi("/openapi/{documentName}.json");

app.MapGet("/healthz", async (IDatabaseHealth database, CancellationToken cancellationToken) =>
{
    if (await database.IsHealthyAsync(cancellationToken))
        return Results.Ok(new { api = "ok", database = "ok" });

    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Service unavailable", detail: "Database unavailable", extensions: new Dictionary<string, object?>
        { ["code"] = "database_unavailable", ["database"] = "unavailable" });
}).Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

app.Run();

public interface IDatabaseHealth { Task<bool> IsHealthyAsync(CancellationToken cancellationToken); }

public sealed class DatabaseHealth(IConfiguration configuration, ILogger<DatabaseHealth> logger) : IDatabaseHealth
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"];
        if (string.IsNullOrWhiteSpace(connectionString)) return false;
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("select 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception)
        {
            logger.LogWarning("Database health check failed with code {ErrorCode}", "database_unavailable");
            return false;
        }
    }
}

public partial class Program { }
