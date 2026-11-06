using System.Text.Json.Serialization;
using FleetOps.Api;
using FleetOps.Api.Auth;
using FleetOps.Api.Pipeline;
using FleetOps.Identity;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(LogLevel.None);
builder.Logging.AddFilter((category, level) => category.StartsWith("FleetOps.", StringComparison.Ordinal) || category == "Microsoft.Hosting.Lifetime");
builder.Logging.AddFilter((category, level) => category.StartsWith("FleetOps.", StringComparison.Ordinal) || category == "Microsoft.Hosting.Lifetime");
builder.Services.PostConfigure<LoggerFilterOptions>(options =>
{
    options.Rules.Add(new LoggerFilterRule(null, null, LogLevel.Trace, static (_, category, _) => category is not null && (category.StartsWith("FleetOps.", StringComparison.Ordinal) || category == "Microsoft.Hosting.Lifetime")));
    options.Rules.Add(new LoggerFilterRule(null, "Npgsql", LogLevel.None, null));
});
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions.TryAdd("code", context.HttpContext.Items["ProblemCode"] ?? "unexpected_error"));
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);
builder.Services.AddOpenApi();
builder.Services.AddHttpContextAccessor();
builder.Services.AddDbContext<FleetOpsDbContext>(options =>
{
    var connectionString = DatabaseConnectionString.Resolve(builder.Configuration);
    if (string.IsNullOrWhiteSpace(connectionString)) options.UseNpgsql();
    else MigrationDbContextOptions.Configure(options, connectionString);
});
builder.Services.AddIdentityCore<OfficeUser>(options =>
{
    options.Password.RequiredLength = 12; options.Password.RequireDigit = false; options.Password.RequireLowercase = false; options.Password.RequireUppercase = false; options.Password.RequireNonAlphanumeric = false; options.Password.RequiredUniqueChars = 1; options.Lockout.MaxFailedAccessAttempts = 5; options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<FleetOpsDbContext>().AddTokenProvider<AuthenticatorTokenProvider<OfficeUser>>(TokenOptions.DefaultAuthenticatorProvider);
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<SessionAccessor>();
builder.Services.AddScoped<IAccessContextAccessor>(services => services.GetRequiredService<SessionAccessor>());
builder.Services.AddScoped<IStepUpContextAccessor>(services => services.GetRequiredService<SessionAccessor>());
builder.Services.AddSingleton<IMailbox, DemoMailbox>();
builder.Services.AddSingleton<ICompanyTimeService>(_ => new CompanyTimeService(CompanyTimeService.Resolve(builder.Configuration["Company:Timezone"] ?? "Europe/Berlin")));
builder.Services.AddSingleton<IDatabaseHealth, DatabaseHealth>();
builder.Services.AddSingleton<ICommandPolicyProvider, DefaultCommandPolicyProvider>();
builder.Services.AddSingleton<ICommandTransactionFactory, NpgsqlCommandTransactionFactory>();
builder.Services.AddScoped<ISubjectRoleLookup, FailClosedSubjectRoleLookup>();
builder.Services.AddScoped<CommandDispatcher>();
builder.Services.AddScoped<QueryDispatcher>();
builder.Services.AddScoped<IQueryHandler<GetVehicleQuery, VehicleDetail>, VehicleQueryHandler>();
builder.Services.AddScoped<IVehicleStore, EfVehicleStore>();
builder.Services.AddScoped<ICommandStatePrecondition<EditVehicleCommand>, RetiredVehicleEditPrecondition>();
builder.Services.AddScoped<ICommandHandler<CreateVehicleCommand, VehicleDetail>, CreateVehicleHandler>();
builder.Services.AddScoped<ICommandHandler<EditVehicleCommand, VehicleDetail>, EditVehicleHandler>();

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
            var status = refusal.Code == "not_signed_in" ? 401 : refusal.Code == "state_conflict" ? 409 : 403;
            await Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = refusal.Code, ["allowedRoles"] = refusal.AllowedRoles.Select(x => x.Code()).ToArray() }).ExecuteAsync(context); return;
        }
        if (exception is InvalidOperationException { Message: "mailbox_unavailable" }) { await Results.Json(new { code = "mailbox_unavailable" }, statusCode: 503).ExecuteAsync(context); return; }
        if (exception is VehicleValidationException validation) { await Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "validation_error", ["fields"] = validation.Fields }).ExecuteAsync(context); return; }
        if (exception is VehicleConflictException conflict) { await Results.Problem(statusCode: conflict.Code is "duplicate_vehicle" or "version_conflict" or "state_conflict" ? 409 : 404, extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }).ExecuteAsync(context); return; }
        try { app.Logger.LogError("Unhandled exception {ExceptionType} TraceId {TraceId}", exception?.GetType().Name ?? "Unknown", context.TraceIdentifier); } catch { }
        context.Response.StatusCode = 500; await Results.Problem(statusCode: 500).ExecuteAsync(context);
    }
});
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi("/openapi/{documentName}.json");
app.UseMiddleware<SessionMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1") && OriginGuard.IsUnsafe(context.Request) && !OriginGuard.IsAllowed(context.Request, context.RequestServices.GetRequiredService<IConfiguration>()["Auth:PublicOrigin"])) { await Results.Json(new { code = "origin_refused" }, statusCode: 403).ExecuteAsync(context); return; }
    if (context.Request.Path.StartsWithSegments("/api/v1") && !context.Request.Path.StartsWithSegments("/api/v1/auth") && context.RequestServices.GetRequiredService<IAccessContextAccessor>().Current is null)
    { var code = context.Items.TryGetValue("ProblemCode", out var value) && value is string text ? text : "not_signed_in"; await Results.Json(new { code }, statusCode: 401).ExecuteAsync(context); return; }
    await next();
});
app.MapGet("/healthz", async (IDatabaseHealth database, CancellationToken cancellationToken) => await database.IsHealthyAsync(cancellationToken) ? Results.Ok(new { api = "ok", database = "ok" }) : Results.Problem(statusCode: 503, title: "Service unavailable", detail: "Database unavailable", extensions: new Dictionary<string, object?> { ["code"] = "database_unavailable", ["database"] = "unavailable" }));
AuthEndpoints.Map(app);
app.MapPost("/api/v1/fleet/vehicles", async (CreateVehicleCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken) => { var result = await dispatcher.DispatchAsync<CreateVehicleCommand, VehicleDetail>(command, cancellationToken); return Results.Created($"/api/v1/fleet/vehicles/{result.Id}", result); });
app.MapPut("/api/v1/fleet/vehicles/{id:guid}", async (Guid id, EditVehicleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<EditVehicleCommand, VehicleDetail>(request.ToCommand(id), cancellationToken)));
app.MapGet("/api/v1/fleet/vehicles/{id:guid}", async (Guid id, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetVehicleQuery, VehicleDetail>(new(id), cancellationToken)));
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapPost("/__tests/step-up-command", (IAccessContextAccessor access, IStepUpContextAccessor stepUp) => access.Current is null ? Results.Json(new { code = "not_signed_in" }, statusCode: 401) : stepUp.HasFreshStepUp ? Results.Ok(new { accepted = true }) : Results.Json(new { code = "step_up_required" }, statusCode: 403));
    app.MapPost("/__tests/dispatcher-command", (IAccessContextAccessor access) => access.Current is null ? Results.Json(new { code = "not_signed_in" }, statusCode: 401) : Results.Ok(new { actor = access.Current.EmployeeId }));
}
app.Run();

public interface IDatabaseHealth { Task<bool> IsHealthyAsync(CancellationToken cancellationToken); }
public sealed class DatabaseHealth(IConfiguration configuration, ILogger<DatabaseHealth> logger) : IDatabaseHealth
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken) { var cs = DatabaseConnectionString.Resolve(configuration); if (string.IsNullOrWhiteSpace(cs)) return false; try { await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("select 1", connection); await command.ExecuteScalarAsync(cancellationToken); return true; } catch { logger.LogWarning("Database health check failed with code {ErrorCode}", "database_unavailable"); return false; } }
}
public sealed record EditVehicleRequest(int Version, VehicleKind Kind, string? Plate, string? Vin, string? Make, string? Model, int Year, int GrossWeightKg, string? RequiredLicenceClass, VehicleOwnership Ownership, DateOnly? LeaseEndDate, TrailerType? TrailerType, int? OdometerKm, Guid? DepotId, int? ServiceIntervalKm, int? ServiceIntervalMonths, DateOnly? LastTachoDownloadAt, string? Reason)
{ public EditVehicleCommand ToCommand(Guid id) => new(id, Version, Kind, Plate, Vin, Make, Model, Year, GrossWeightKg, RequiredLicenceClass, Ownership, LeaseEndDate, TrailerType, OdometerKm, DepotId, ServiceIntervalKm, ServiceIntervalMonths, LastTachoDownloadAt, Reason); }
public sealed class VehicleQueryHandler(IConfiguration configuration) : IQueryHandler<GetVehicleQuery, VehicleDetail>
{ public async Task<VehicleDetail> HandleAsync(GetVehicleQuery query, AccessContext actor, CancellationToken cancellationToken) => await EfVehicleStore.ReadByConnectionAsync(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured."), query.SubjectId ?? throw new VehicleValidationException(["id"]), cancellationToken); }
public partial class Program { }
