using System.Text.Json.Serialization;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Api.Pipeline;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using FleetOps.Api.Auth;
using FleetOps.Identity;
using FleetOps.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions.TryAdd("code", context.HttpContext.Items["ProblemCode"] ?? "unexpected_error");
});
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<FleetOpsDbContext>(options =>
    FleetOps.Migrations.MigrationDbContextOptions.Configure(options, DatabaseConnectionString.Resolve(builder.Configuration) ?? "Host=localhost;Database=fleetops;Username=postgres;Password=postgres"));
builder.Services.AddIdentityCore<OfficeUser>(options =>
{
    options.Password.RequiredLength = 12;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredUniqueChars = 1;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<FleetOpsDbContext>()
  .AddTokenProvider<AuthenticatorTokenProvider<OfficeUser>>(TokenOptions.DefaultAuthenticatorProvider);
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SessionAccessor>();
builder.Services.AddScoped<IAccessContextAccessor>(services => services.GetRequiredService<SessionAccessor>());
builder.Services.AddScoped<IStepUpContextAccessor>(services => services.GetRequiredService<SessionAccessor>());
builder.Services.AddSingleton<IMailbox, DemoMailbox>();
builder.Services.AddSingleton<ICompanyTimeService>(_ =>
    new CompanyTimeService(CompanyTimeService.Resolve(builder.Configuration["Company:Timezone"] ?? "Europe/Berlin")));
builder.Services.AddSingleton<IDatabaseHealth, DatabaseHealth>();
builder.Services.AddSingleton<ICommandPolicyProvider, DefaultCommandPolicyProvider>();
builder.Services.AddSingleton<ICommandTransactionFactory, NpgsqlCommandTransactionFactory>();
builder.Services.AddScoped<ISubjectRoleLookup, FailClosedSubjectRoleLookup>();
builder.Services.AddScoped<CommandDispatcher>();
builder.Services.AddScoped<QueryDispatcher>();

var app = builder.Build();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    SuppressDiagnosticsCallback = _ => true,
    ExceptionHandler = async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is AuthorizationException refusal)
        {
            context.Items["ProblemCode"] = refusal.Code;
            var status = refusal.Code == "not_signed_in" ? StatusCodes.Status401Unauthorized :
                refusal.Code == "state_conflict" ? StatusCodes.Status409Conflict :
                StatusCodes.Status403Forbidden;
            await Results.Problem(statusCode: status, extensions: new Dictionary<string, object?>
            {
                ["code"] = refusal.Code,
                ["allowedRoles"] = refusal.AllowedRoles.Select(role => role.Code()).ToArray()
            }).ExecuteAsync(context);
            return;
        }
        if (exception is InvalidOperationException { Message: "mailbox_unavailable" })
        {
            await Results.Json(new { code = "mailbox_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable).ExecuteAsync(context);
            return;
        }
        try
        {
            app.Logger.LogError("Unhandled exception {ExceptionType} TraceId {TraceId}",
                exception?.GetType().Name ?? "Unknown", context.TraceIdentifier);
        }
        catch (Exception)
        {
            // A logging sink must not turn the sanitized error response into a second failure.
        }
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await Results.Problem(statusCode: StatusCodes.Status500InternalServerError).ExecuteAsync(context);
    }
});
app.MapOpenApi("/openapi/{documentName}.json");

app.UseMiddleware<SessionMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1") && OriginGuard.IsUnsafe(context.Request))
    {
        if (!OriginGuard.IsAllowed(context.Request))
        {
            await Results.Json(new { code = "origin_refused" }, statusCode: StatusCodes.Status403Forbidden).ExecuteAsync(context);
            return;
        }
    }
    if (context.Request.Path.StartsWithSegments("/api/v1") && !context.Request.Path.StartsWithSegments("/api/v1/auth"))
    {
        if (context.RequestServices.GetRequiredService<IAccessContextAccessor>().Current is null)
        {
            var code = context.Items.TryGetValue("ProblemCode", out var problemCode) && problemCode is string value ? value : "not_signed_in";
            await Results.Json(new { code }, statusCode: StatusCodes.Status401Unauthorized).ExecuteAsync(context);
            return;
        }
    }
    await next();
});

app.MapGet("/healthz", async (IDatabaseHealth database, CancellationToken cancellationToken) =>
{
    if (await database.IsHealthyAsync(cancellationToken))
        return Results.Ok(new { api = "ok", database = "ok" });

    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
        title: "Service unavailable", detail: "Database unavailable", extensions: new Dictionary<string, object?>
        { ["code"] = "database_unavailable", ["database"] = "unavailable" });
}).Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status503ServiceUnavailable);

AuthEndpoints.Map(app);
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapPost("/__tests/step-up-command", (IAccessContextAccessor access, IStepUpContextAccessor stepUp) =>
        access.Current is null ? Results.Json(new { code = "not_signed_in" }, statusCode: 401) :
        stepUp.HasFreshStepUp ? Results.Ok(new { accepted = true }) :
        Results.Json(new { code = "step_up_required" }, statusCode: 403));
    app.MapPost("/__tests/dispatcher-command", (IAccessContextAccessor access) =>
        access.Current is null ? Results.Json(new { code = "not_signed_in" }, statusCode: 401) :
        Results.Ok(new { actor = access.Current.EmployeeId }));
}

app.Run();

public interface IDatabaseHealth { Task<bool> IsHealthyAsync(CancellationToken cancellationToken); }

public sealed class DatabaseHealth(IConfiguration configuration, ILogger<DatabaseHealth> logger) : IDatabaseHealth
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        var connectionString = DatabaseConnectionString.Resolve(configuration);
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
