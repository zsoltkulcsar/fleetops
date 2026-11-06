using System.Text.Json.Serialization;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
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
using Microsoft.AspNetCore.DataProtection;
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
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
});
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
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ICommandTransactionFactory, NpgsqlCommandTransactionFactory>();
builder.Services.AddScoped<ISubjectRoleLookup, FailClosedSubjectRoleLookup>();
builder.Services.AddScoped<CommandDispatcher>();
builder.Services.AddScoped<QueryDispatcher>();
builder.Services.AddScoped<IQueryHandler<GetVehicleQuery, VehicleDetail>, VehicleQueryHandler>();
builder.Services.AddScoped<IQueryHandler<ListVehiclesQuery, VehicleListResult>, VehicleListQueryHandler>();
builder.Services.AddScoped<IVehicleStore, EfVehicleStore>();
builder.Services.AddScoped<ICommandStatePrecondition<EditVehicleCommand>, RetiredVehicleEditPrecondition>();
builder.Services.AddScoped<ICommandStatePrecondition<RetireVehicleCommand>, ActiveVehicleRetirePrecondition>();
builder.Services.AddScoped<ICommandHandler<CreateVehicleCommand, VehicleDetail>, CreateVehicleHandler>();
builder.Services.AddScoped<ICommandHandler<EditVehicleCommand, VehicleDetail>, EditVehicleHandler>();
builder.Services.AddScoped<ICommandHandler<RetireVehicleCommand, VehicleDetail>, RetireVehicleHandler>();
builder.Services.AddScoped<ICommandHandler<UndoRetireVehicleCommand, VehicleDetail>, UndoRetireVehicleHandler>();

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
        var jsonException = VehicleJsonException.Find(exception);
        if (context.Request.Method is "POST" or "PUT" && context.Request.Path.StartsWithSegments("/api/v1/fleet/vehicles") && jsonException is not null)
        {
            var field = VehicleJsonException.FieldName(jsonException.Path);
            await Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "validation_error", ["fields"] = field is null ? Array.Empty<string>() : new[] { field } }).ExecuteAsync(context); return;
        }
        if (exception is InvalidCursorException) { await Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "invalid_cursor" }).ExecuteAsync(context); return; }
        if (exception is VehicleConflictException conflict) { await Results.Problem(statusCode: conflict.Code is "duplicate_vehicle" or "version_conflict" or "state_conflict" or "undo_window_closed" or "plate_in_use" ? 409 : 404, extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }).ExecuteAsync(context); return; }
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
app.MapPost("/api/v1/fleet/vehicles", async (CreateVehicleCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken) => { var result = await dispatcher.DispatchAsync<CreateVehicleCommand, VehicleDetail>(command, cancellationToken); return Results.Created($"/api/v1/fleet/vehicles/{result.Id}", result); }).Produces<VehicleDetail>(StatusCodes.Status201Created);
app.MapPut("/api/v1/fleet/vehicles/{id:guid}", async (Guid id, EditVehicleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<EditVehicleCommand, VehicleDetail>(request.ToCommand(id), cancellationToken))).Produces<VehicleDetail>();
app.MapPost("/api/v1/fleet/vehicles/{id:guid}/retire", async (Guid id, RetireVehicleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<RetireVehicleCommand, VehicleDetail>(new(id, request.Version, request.RetiredReason), cancellationToken))).Produces<VehicleDetail>();
app.MapPost("/api/v1/fleet/vehicles/{id:guid}/undo-retire", async (Guid id, UndoRetireVehicleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<UndoRetireVehicleCommand, VehicleDetail>(new(id, request.Version, request.Reason), cancellationToken))).Produces<VehicleDetail>();
app.MapGet("/api/v1/fleet/vehicles", async (string? cursor, int? limit, string? search, string? kind, bool? includeRetired, QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
{
    VehicleKind? parsedKind = null;
    if (!string.IsNullOrWhiteSpace(kind))
    {
        var value = kind.Trim().ToLowerInvariant() switch
        {
            "truck" => VehicleKind.Truck,
            "trailer" => VehicleKind.Trailer,
            _ => throw new VehicleValidationException(["kind"])
        };
        parsedKind = value;
    }
    var query = new ListVehiclesQuery(cursor, limit is null ? 50 : Math.Clamp(limit.Value, 1, 100), search, parsedKind?.ToString(), includeRetired == true);
    return Results.Ok(await dispatcher.DispatchAsync<ListVehiclesQuery, VehicleListResult>(query, cancellationToken));
}).Produces<VehicleListResult>();
app.MapGet("/api/v1/fleet/vehicles/{id:guid}", async (Guid id, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetVehicleQuery, VehicleDetail>(new(id), cancellationToken))).Produces<VehicleDetail>();
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
public sealed record RetireVehicleRequest(int Version, RetiredReason RetiredReason);
public sealed record UndoRetireVehicleRequest(int Version, string? Reason);
public sealed class VehicleQueryHandler(IConfiguration configuration) : IQueryHandler<GetVehicleQuery, VehicleDetail>
{ public async Task<VehicleDetail> HandleAsync(GetVehicleQuery query, AccessContext actor, CancellationToken cancellationToken) => await EfVehicleStore.ReadByConnectionAsync(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured."), query.SubjectId ?? throw new VehicleValidationException(["id"]), cancellationToken); }

public sealed class VehicleListQueryHandler(IConfiguration configuration, IDataProtectionProvider protectionProvider) : IQueryHandler<ListVehiclesQuery, VehicleListResult>
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("FleetOps.VehicleListCursor.v1");

    public async Task<VehicleListResult> HandleAsync(ListVehiclesQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var cursor = Unprotect(query.Cursor);
        var page = await EfVehicleStore.ReadListByConnectionAsync(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured."), query, cursor, cancellationToken);
        var nextCursor = page.NextCursor is null ? null : protector.Protect(JsonSerializer.Serialize(page.NextCursor));
        return new(page.Items, nextCursor);
    }

    private VehicleListCursor? Unprotect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        try
        {
            var cursor = JsonSerializer.Deserialize<VehicleListCursor>(protector.Unprotect(value));
            if (cursor is null || string.IsNullOrWhiteSpace(cursor.Plate) || cursor.Id == Guid.Empty)
                throw new InvalidCursorException();
            return cursor;
        }
        catch (InvalidCursorException)
        {
            throw;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or JsonException)
        {
            throw new InvalidCursorException();
        }
    }
}
public partial class Program { }

internal static class VehicleJsonException
{
    public static JsonException? Find(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is JsonException found)
                return found;

            exception = exception.InnerException;
        }

        return null;
    }

    public static string? FieldName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("$.", StringComparison.Ordinal))
            return null;

        var field = path[2..].Split('.', '[', StringSplitOptions.RemoveEmptyEntries)[0];
        return string.IsNullOrWhiteSpace(field) ? null : field;
    }
}
