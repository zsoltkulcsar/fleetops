using System.Text.Json.Serialization;
using FleetOps.Api;
using FleetOps.Api.Pipeline;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Fleet;
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
builder.Services.AddSingleton<ICompanyTimeService>(_ => new CompanyTimeService(CompanyTimeService.Resolve(builder.Configuration["Company:Timezone"] ?? "Europe/Berlin")));
builder.Services.AddSingleton<IDatabaseHealth, DatabaseHealth>(); builder.Services.AddSingleton<IAccessContextAccessor, NoSessionAccessor>(); builder.Services.AddSingleton<ICommandPolicyProvider, DefaultCommandPolicyProvider>(); builder.Services.AddSingleton<ICommandTransactionFactory, NpgsqlCommandTransactionFactory>(); builder.Services.AddScoped<ISubjectRoleLookup, FailClosedSubjectRoleLookup>(); builder.Services.AddScoped<CommandDispatcher>(); builder.Services.AddScoped<QueryDispatcher>();
builder.Services.AddScoped<IVehicleStore, EfVehicleStore>(); builder.Services.AddScoped<ICommandHandler<CreateVehicleCommand, VehicleDetail>, CreateVehicleHandler>(); builder.Services.AddScoped<ICommandHandler<EditVehicleCommand, VehicleDetail>, EditVehicleHandler>();
var app = builder.Build();
app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true, ExceptionHandler = async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (exception is AuthorizationException refusal) { context.Items["ProblemCode"] = refusal.Code; var status = refusal.Code == RefusalCodes.NotSignedIn ? 401 : refusal.Code == RefusalCodes.StateConflict ? 409 : 403; await Results.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = refusal.Code, ["allowedRoles"] = refusal.AllowedRoles.Select(x => x.Code()).ToArray() }).ExecuteAsync(context); return; }
    if (exception is VehicleValidationException validation) { await Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "validation_error", ["fields"] = validation.Fields }).ExecuteAsync(context); return; }
    if (exception is VehicleConflictException conflict) { await Results.Problem(statusCode: conflict.Code is "duplicate_vehicle" or "version_conflict" ? 409 : 404, extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }).ExecuteAsync(context); return; }
    app.Logger.LogError("Unhandled exception {ExceptionType} TraceId {TraceId}", exception?.GetType().Name ?? "Unknown", context.TraceIdentifier);
    context.Response.StatusCode = 500; await Results.Problem(statusCode: 500).ExecuteAsync(context);
} });
app.MapOpenApi("/openapi/{documentName}.json");
app.MapGet("/healthz", async (IDatabaseHealth database, CancellationToken cancellationToken) => await database.IsHealthyAsync(cancellationToken)
    ? Results.Ok(new { api = "ok", database = "ok" })
    : Results.Problem(statusCode: 503, title: "Service unavailable", detail: "Database unavailable",
        extensions: new Dictionary<string, object?> { ["code"] = "database_unavailable", ["database"] = "unavailable" }));
app.MapPost("/api/v1/fleet/vehicles", async (CreateVehicleCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken) => { var result = await dispatcher.DispatchAsync<CreateVehicleCommand, VehicleDetail>(command, cancellationToken); return Results.Created($"/api/v1/fleet/vehicles/{result.Id}", result); });
app.MapPut("/api/v1/fleet/vehicles/{id:guid}", async (Guid id, EditVehicleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<EditVehicleCommand, VehicleDetail>(request.ToCommand(id), cancellationToken)));
app.MapGet("/api/v1/fleet/vehicles/{id:guid}", async (Guid id, IConfiguration configuration, CancellationToken cancellationToken) => { var connectionString = DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured."); return Results.Ok(await EfVehicleStore.ReadByConnectionAsync(connectionString, id, cancellationToken)); });
app.Run();

public interface IDatabaseHealth { Task<bool> IsHealthyAsync(CancellationToken cancellationToken); }
public sealed class DatabaseHealth(IConfiguration configuration, ILogger<DatabaseHealth> logger) : IDatabaseHealth
{
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken) { var cs = DatabaseConnectionString.Resolve(configuration); if (string.IsNullOrWhiteSpace(cs)) return false; try { await using var connection = new NpgsqlConnection(cs); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("select 1", connection); await command.ExecuteScalarAsync(cancellationToken); return true; } catch { logger.LogWarning("Database health check failed with code {ErrorCode}", "database_unavailable"); return false; } }
}
public sealed record EditVehicleRequest(int Version, VehicleKind Kind, string Plate, string Vin, string Make, string Model, int Year, int GrossWeightKg, string? RequiredLicenceClass, VehicleOwnership Ownership, DateOnly? LeaseEndDate, TrailerType? TrailerType, int? OdometerKm, Guid? DepotId, int? ServiceIntervalKm, int? ServiceIntervalMonths, DateOnly? LastTachoDownloadAt, string Reason)
{ public EditVehicleCommand ToCommand(Guid id) => new(id, Version, Kind, Plate, Vin, Make, Model, Year, GrossWeightKg, RequiredLicenceClass, Ownership, LeaseEndDate, TrailerType, OdometerKm, DepotId, ServiceIntervalKm, ServiceIntervalMonths, LastTachoDownloadAt, Reason); }
public partial class Program { }
