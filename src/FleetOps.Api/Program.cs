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
using FleetOps.Modules.Documents;
using FleetOps.Modules.Readiness;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Setup;
using FleetOps.Api.Files;
using FleetOps.Api.Jobs;
using FleetOps.Kernel.Files;
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
builder.Services.AddScoped<IReadiness, EfReadinessStore>();
builder.Services.AddSingleton<ICommandPolicyProvider, DefaultCommandPolicyProvider>();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<ICommandTransactionFactory, NpgsqlCommandTransactionFactory>();
builder.Services.AddScoped<ISubjectRoleLookup, FailClosedSubjectRoleLookup>();
builder.Services.AddScoped<CommandDispatcher>();
builder.Services.AddScoped<IChangedSubjects, ChangedSubjects>();
builder.Services.AddScoped<IPostCommandEvaluation, AlertPostCommandEvaluation>();
builder.Services.AddScoped<IAlertEngine, AlertEngine>();
builder.Services.AddScoped<ISettings, EfSettingsStore>();
builder.Services.AddScoped<IAlertEvaluator, A02EmployeeEvaluator>();
builder.Services.AddScoped<IAlertEvaluator, A03DocumentEvaluator>();
builder.Services.AddScoped<IAlertEvaluator, A05VehicleEvaluator>();
builder.Services.AddScoped<QueryDispatcher>();
builder.Services.AddScoped<JobRunner>();
builder.Services.AddScoped<IJob, FileOrphansJob>();
builder.Services.AddScoped<IJob, AlertsSweepJob>();
builder.Services.AddScoped<ICommandHandler<RunJobCommand, JobRunResult>, RunJobCommandHandler>();
builder.Services.AddScoped<IQueryHandler<ListJobsQuery, JobListResult>, ListJobsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetVehicleQuery, VehicleDetail>, VehicleQueryHandler>();
builder.Services.AddScoped<IQueryHandler<ListVehiclesQuery, VehicleListResult>, VehicleListQueryHandler>();
builder.Services.AddScoped<IVehicleStore, EfVehicleStore>();
builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection("Storage"));
builder.Services.AddScoped<IObjectStore, S3ObjectStore>();
builder.Services.AddScoped<IDocumentStore, EfDocumentStore>();
builder.Services.AddScoped<ICommandHandler<RequestEmployeeDocumentUploadCommand, UploadLinkResult>, RequestEmployeeDocumentUploadHandler>();
builder.Services.AddScoped<ICommandHandler<RequestVehicleDocumentUploadCommand, UploadLinkResult>, RequestVehicleDocumentUploadHandler>();
builder.Services.AddScoped<ICommandHandler<ConfirmEmployeeDocumentCommand, DocumentDetail>, ConfirmEmployeeDocumentHandler>();
builder.Services.AddScoped<ICommandHandler<ConfirmVehicleDocumentCommand, DocumentDetail>, ConfirmVehicleDocumentHandler>();
builder.Services.AddScoped<ICommandHandler<VerifyEmployeeDocumentCommand, DocumentDetail>, VerifyEmployeeDocumentHandler>();
builder.Services.AddScoped<ICommandHandler<VerifyVehicleDocumentCommand, DocumentDetail>, VerifyVehicleDocumentHandler>();
builder.Services.AddScoped<ICommandHandler<RejectEmployeeDocumentCommand, DocumentDetail>, RejectEmployeeDocumentHandler>();
builder.Services.AddScoped<ICommandHandler<RejectVehicleDocumentCommand, DocumentDetail>, RejectVehicleDocumentHandler>();
builder.Services.AddScoped<ICommandHandler<DeleteDocumentCommand, DocumentDetail>, DeleteDocumentHandler>();
builder.Services.AddScoped<ICommandHandler<SightCheckDocumentCommand, DocumentDetail>, SightCheckDocumentHandler>();
builder.Services.AddScoped<IQueryHandler<ListDocumentsQuery, DocumentListResult>, DocumentListQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetDocumentQuery, DocumentDetail>, DocumentDetailQueryHandler>();
builder.Services.AddScoped<IQueryHandler<LookupPeopleQuery, PeopleLookupResult>, PeopleLookupQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetDocumentCountsQuery, DocumentCounts>, DocumentCountsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<ListMissingDocumentsQuery, DocumentMissingResult>, MissingDocumentQueryHandler>();
builder.Services.AddScoped<IQueryHandler<SearchDocumentsQuery, DocumentSearchResult>, SearchDocumentsQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetDocumentFileLinkQuery, DocumentFileLinkResult>, DocumentFileLinkQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetReadinessSummaryQuery, ReadinessSummary>, ReadinessSummaryQueryHandler>();
builder.Services.AddScoped<IQueryHandler<ListDriverReadinessQuery, DriverReadinessList>, DriverReadinessQueryHandler>();
builder.Services.AddScoped<IQueryHandler<GetAttentionQuery, AttentionResult>, AttentionQueryHandler>();
builder.Services.AddScoped<ICommandHandler<SnoozeAlertCommand, AlertActionResult>, AlertCommandHandler>();
builder.Services.AddScoped<ICommandHandler<UnsnoozeAlertCommand, AlertActionResult>, AlertCommandHandler>();
builder.Services.AddScoped<ICommandStatePrecondition<EditVehicleCommand>, RetiredVehicleEditPrecondition>();
builder.Services.AddScoped<ICommandStatePrecondition<RetireVehicleCommand>, ActiveVehicleRetirePrecondition>();
builder.Services.AddScoped<ICommandHandler<CreateVehicleCommand, VehicleDetail>, CreateVehicleHandler>();
builder.Services.AddScoped<ICommandHandler<EditVehicleCommand, VehicleDetail>, EditVehicleHandler>();
builder.Services.AddScoped<ICommandHandler<RetireVehicleCommand, VehicleDetail>, RetireVehicleHandler>();
builder.Services.AddScoped<ICommandHandler<UndoRetireVehicleCommand, VehicleDetail>, UndoRetireVehicleHandler>();
builder.Services.AddScoped<ICommandHandler<SetVehicleOffRoadCommand, VehicleDetail>, SetVehicleOffRoadHandler>();

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
        if (exception is StorageUnavailableException) { await Results.Problem(statusCode: 503, extensions: new Dictionary<string, object?> { ["code"] = "storage_unavailable" }).ExecuteAsync(context); return; }
        if (exception is DocumentValidationException documentValidation) { await Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = documentValidation.Code, ["fields"] = documentValidation.Fields }).ExecuteAsync(context); return; }
        var jsonException = VehicleJsonException.Find(exception);
        if (context.Request.Method is "POST" or "PUT" && (context.Request.Path.StartsWithSegments("/api/v1/fleet/vehicles") || context.Request.Path.StartsWithSegments("/api/v1/documents")) && jsonException is not null)
        {
            var field = VehicleJsonException.FieldName(jsonException.Path);
            await Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "validation_error", ["fields"] = field is null ? Array.Empty<string>() : new[] { field } }).ExecuteAsync(context); return;
        }
        if (exception is InvalidCursorException) { await Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "invalid_cursor" }).ExecuteAsync(context); return; }
        if (exception is DocumentInvalidCursorException) { await Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "invalid_cursor" }).ExecuteAsync(context); return; }
        if (exception is VehicleConflictException conflict) { await Results.Problem(statusCode: conflict.Code is "duplicate_vehicle" or "version_conflict" or "state_conflict" or "undo_window_closed" or "plate_in_use" ? 409 : 404, extensions: new Dictionary<string, object?> { ["code"] = conflict.Code }).ExecuteAsync(context); return; }
        if (exception is DocumentConflictException documentConflict) { await Results.Problem(statusCode: documentConflict.Code is "state_conflict" or "version_conflict" ? 409 : 404, extensions: new Dictionary<string, object?> { ["code"] = documentConflict.Code }).ExecuteAsync(context); return; }
        if (exception is AlertValidationException alertValidation) { await Results.Problem(statusCode: 400, extensions: new Dictionary<string, object?> { ["code"] = "validation_error", ["fields"] = alertValidation.Fields }).ExecuteAsync(context); return; }
        if (exception is AlertConflictException alertConflict) { await Results.Problem(statusCode: alertConflict.Code is "state_conflict" or "version_conflict" or "not_snoozable" ? 409 : 404, extensions: new Dictionary<string, object?> { ["code"] = alertConflict.Code }).ExecuteAsync(context); return; }
        try { app.Logger.LogError("Unhandled exception {ExceptionType} TraceId {TraceId}", exception?.GetType().Name ?? "Unknown", context.TraceIdentifier); } catch { }
        context.Response.StatusCode = 500; await Results.Problem(statusCode: 500).ExecuteAsync(context);
    }
});
app.UseMiddleware<SessionMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1") && OriginGuard.IsUnsafe(context.Request) && !OriginGuard.IsAllowed(context.Request, context.RequestServices.GetRequiredService<IConfiguration>()["Auth:PublicOrigin"])) { await Results.Json(new { code = "origin_refused" }, statusCode: 403).ExecuteAsync(context); return; }
    if (context.Request.Path.StartsWithSegments("/api/v1") && !context.Request.Path.StartsWithSegments("/api/v1/auth") && context.RequestServices.GetRequiredService<IAccessContextAccessor>().Current is null)
    { var code = context.Items.TryGetValue("ProblemCode", out var value) && value is string text ? text : "not_signed_in"; await Results.Json(new { code }, statusCode: 401).ExecuteAsync(context); return; }
    await next();
});
app.MapGet("/jobs/{name}", async (string name, HttpContext context, JobRunner runner, CancellationToken cancellationToken) => await JobEndpoints.RunAsync(name, JobTrigger.scheduler, context, runner, cancellationToken)).ExcludeFromDescription();
app.MapGet("/jobs/nightly", async (HttpContext context, JobRunner runner, CancellationToken cancellationToken) => await JobEndpoints.RunNightlyAsync(context, runner, cancellationToken)).ExcludeFromDescription();
app.MapPost("/api/v1/admin/jobs/{name}/run", async (string name, CommandDispatcher dispatcher, JobRunner runner, IConfiguration configuration, CancellationToken cancellationToken) => !configuration.GetValue("Jobs:AdminTrigger", false) || !runner.Contains(name) ? Results.NotFound() : Results.Ok(await dispatcher.DispatchAsync<RunJobCommand, JobRunResult>(new(name), cancellationToken))).Produces<JobRunResult>();
app.MapGet("/api/v1/admin/jobs", async (QueryDispatcher dispatcher, IConfiguration configuration, CancellationToken cancellationToken) => !configuration.GetValue("Jobs:AdminTrigger", false) ? Results.NotFound() : Results.Ok(await dispatcher.DispatchAsync<ListJobsQuery, JobListResult>(new(), cancellationToken))).Produces<JobListResult>();
app.MapGet("/healthz", async (IDatabaseHealth database, CancellationToken cancellationToken) => await database.IsHealthyAsync(cancellationToken) ? Results.Ok(new { api = "ok", database = "ok" }) : Results.Problem(statusCode: 503, title: "Service unavailable", detail: "Database unavailable", extensions: new Dictionary<string, object?> { ["code"] = "database_unavailable", ["database"] = "unavailable" }));
AuthEndpoints.Map(app);
app.MapPost("/api/v1/fleet/vehicles", async (CreateVehicleCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken) => { var result = await dispatcher.DispatchAsync<CreateVehicleCommand, VehicleDetail>(command, cancellationToken); return Results.Created($"/api/v1/fleet/vehicles/{result.Id}", result); }).Produces<VehicleDetail>(StatusCodes.Status201Created);
app.MapPut("/api/v1/fleet/vehicles/{id:guid}", async (Guid id, EditVehicleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<EditVehicleCommand, VehicleDetail>(request.ToCommand(id), cancellationToken))).Produces<VehicleDetail>();
app.MapPost("/api/v1/fleet/vehicles/{id:guid}/retire", async (Guid id, RetireVehicleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<RetireVehicleCommand, VehicleDetail>(new(id, request.Version, request.RetiredReason), cancellationToken))).Produces<VehicleDetail>();
app.MapPost("/api/v1/fleet/vehicles/{id:guid}/undo-retire", async (Guid id, UndoRetireVehicleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<UndoRetireVehicleCommand, VehicleDetail>(new(id, request.Version, request.Reason), cancellationToken))).Produces<VehicleDetail>();
app.MapPost("/api/v1/fleet/vehicles/{id:guid}/off-road", async (Guid id, SetVehicleOffRoadRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<SetVehicleOffRoadCommand, VehicleDetail>(new(id, request.Version, request.ManualOffRoad, request.Reason), cancellationToken))).Produces<VehicleDetail>();
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
app.MapPost("/api/v1/documents/upload-links", async (DocumentUploadRequest request, HttpContext context, CommandDispatcher dispatcher, CancellationToken cancellationToken) => { context.Response.Headers.CacheControl = "no-store"; if (request.OwnerType == DocumentOwnerType.Employee) return Results.Ok(await dispatcher.DispatchAsync<RequestEmployeeDocumentUploadCommand, UploadLinkResult>(new(request.OwnerId, request.Type, request.ContentType, request.SizeBytes), cancellationToken)); return Results.Ok(await dispatcher.DispatchAsync<RequestVehicleDocumentUploadCommand, UploadLinkResult>(new(request.OwnerId, request.Type, request.ContentType, request.SizeBytes), cancellationToken)); }).Produces<UploadLinkResult>();
app.MapPost("/api/v1/documents", async (DocumentCreateRequest request, HttpContext context, CommandDispatcher dispatcher, CancellationToken cancellationToken) => { context.Response.Headers.CacheControl = "no-store"; DocumentDetail result; if (request.OwnerType == DocumentOwnerType.Employee) result = await dispatcher.DispatchAsync<ConfirmEmployeeDocumentCommand, DocumentDetail>(new(request.OwnerId, request.OwnerId, request.Type, request.Number, request.IssuedAt, request.ExpiresAt, request.Categories, request.Files), cancellationToken); else result = await dispatcher.DispatchAsync<ConfirmVehicleDocumentCommand, DocumentDetail>(new(request.OwnerId, request.OwnerId, request.Type, request.Number, request.IssuedAt, request.ExpiresAt, request.Categories, request.Files), cancellationToken); return Results.Created($"/api/v1/documents/{result.Id}", result); }).Produces<DocumentDetail>(StatusCodes.Status201Created);
app.MapGet("/api/v1/documents", async (DocumentOwnerType? ownerType, Guid? ownerId, DocumentStatus? status, string? search, string? cursor, string? filter, int? limit, QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) => { context.Response.Headers.CacheControl = "no-store"; return Results.Ok(await dispatcher.DispatchAsync<ListDocumentsQuery, DocumentListResult>(new(ownerType, ownerId, status, search, cursor, limit is null ? 50 : Math.Clamp(limit.Value, 1, 100), filter), cancellationToken)); }).Produces<DocumentListResult>();
app.MapGet("/api/v1/documents/{id:guid}", async (Guid id, QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) => { context.Response.Headers.CacheControl = "no-store"; return Results.Ok(await dispatcher.DispatchAsync<GetDocumentQuery, DocumentDetail>(new(id), cancellationToken)); }).Produces<DocumentDetail>();
app.MapGet("/api/v1/documents/counts", async (QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) => { context.Response.Headers.CacheControl = "no-store"; return Results.Ok(await dispatcher.DispatchAsync<GetDocumentCountsQuery, DocumentCounts>(new(), cancellationToken)); }).Produces<DocumentCounts>();
app.MapGet("/api/v1/documents/missing", async (string? cursor, int? limit, QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) => { context.Response.Headers.CacheControl = "no-store"; return Results.Ok(await dispatcher.DispatchAsync<ListMissingDocumentsQuery, DocumentMissingResult>(new(cursor, limit is null ? 50 : Math.Clamp(limit.Value, 1, 100)), cancellationToken)); }).Produces<DocumentMissingResult>();
app.MapGet("/api/v1/documents/{id:guid}/files/{fileId:guid}/link", async (Guid id, Guid fileId, QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) => { context.Response.Headers.CacheControl = "no-store"; var result = await dispatcher.DispatchAsync<GetDocumentFileLinkQuery, DocumentFileLinkResult>(new(id, fileId), cancellationToken); return Results.Ok(new { url = result.Link.Url, expiresAt = result.Link.ExpiresAt }); });
app.MapGet("/api/v1/search", async (string? q, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<SearchDocumentsQuery, DocumentSearchResult>(new(q), cancellationToken))).Produces<DocumentSearchResult>();
app.MapPost("/api/v1/documents/{id:guid}/verify", async (Guid id, DocumentVerifyRequest request, QueryDispatcher queries, CommandDispatcher dispatcher, CancellationToken cancellationToken) => { var detail = await queries.DispatchAsync<GetDocumentQuery, DocumentDetail>(new(id), cancellationToken); return detail.OwnerType == DocumentOwnerType.Employee ? Results.Ok(await dispatcher.DispatchAsync<VerifyEmployeeDocumentCommand, DocumentDetail>(new(id, request.Version, request.ExpiresAt, request.IssuedAt, request.Number, request.Categories), cancellationToken)) : Results.Ok(await dispatcher.DispatchAsync<VerifyVehicleDocumentCommand, DocumentDetail>(new(id, request.Version, request.ExpiresAt, request.IssuedAt, request.Number, request.Categories), cancellationToken)); }).Produces<DocumentDetail>();
app.MapPost("/api/v1/documents/{id:guid}/reject", async (Guid id, DocumentRejectRequest request, QueryDispatcher queries, CommandDispatcher dispatcher, CancellationToken cancellationToken) => { var detail = await queries.DispatchAsync<GetDocumentQuery, DocumentDetail>(new(id), cancellationToken); return detail.OwnerType == DocumentOwnerType.Employee ? Results.Ok(await dispatcher.DispatchAsync<RejectEmployeeDocumentCommand, DocumentDetail>(new(id, request.Version, request.Reason), cancellationToken)) : Results.Ok(await dispatcher.DispatchAsync<RejectVehicleDocumentCommand, DocumentDetail>(new(id, request.Version, request.Reason), cancellationToken)); }).Produces<DocumentDetail>();
app.MapPost("/api/v1/documents/{id:guid}/delete", async (Guid id, DocumentDeleteRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<DeleteDocumentCommand, DocumentDetail>(new(id, request.Version, request.Reason), cancellationToken))).Produces<DocumentDetail>();
app.MapPost("/api/v1/documents/{id:guid}/sight-check", async (Guid id, DocumentSightCheckRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<SightCheckDocumentCommand, DocumentDetail>(new(id, request.Version, request.CheckedAt), cancellationToken))).Produces<DocumentDetail>();
app.MapGet("/api/v1/people/lookup", async (string? search, Guid? id, QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) => { context.Response.Headers.CacheControl = "no-store"; return Results.Ok(await dispatcher.DispatchAsync<LookupPeopleQuery, PeopleLookupResult>(new(search, id), cancellationToken)); }).Produces<PeopleLookupResult>();
app.MapGet("/api/v1/readiness/summary", async (QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetReadinessSummaryQuery, ReadinessSummary>(new(), cancellationToken))).Produces<ReadinessSummary>();
app.MapGet("/api/v1/readiness/drivers", async (string? cursor, int? limit, bool? blockedOnly, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<ListDriverReadinessQuery, DriverReadinessList>(new(cursor, limit is null ? 50 : Math.Clamp(limit.Value, 1, 100), blockedOnly == true), cancellationToken))).Produces<DriverReadinessList>();
app.MapGet("/api/v1/attention", async (QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetAttentionQuery, AttentionResult>(new(), cancellationToken))).Produces<AttentionResult>();
app.MapPost("/api/v1/alerts/{id:guid}/snooze", async (Guid id, SnoozeAlertRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<SnoozeAlertCommand, AlertActionResult>(new(id, request.Version, request.Until, request.Reason), cancellationToken))).Produces<AlertActionResult>();
app.MapPost("/api/v1/alerts/{id:guid}/unsnooze", async (Guid id, UnsnoozeAlertRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<UnsnoozeAlertCommand, AlertActionResult>(new(id, request.Version), cancellationToken))).Produces<AlertActionResult>();
if (app.Environment.IsEnvironment("Testing"))
{
    app.MapPost("/__tests/step-up-command", (IAccessContextAccessor access, IStepUpContextAccessor stepUp) => access.Current is null ? Results.Json(new { code = "not_signed_in" }, statusCode: 401) : stepUp.HasFreshStepUp ? Results.Ok(new { accepted = true }) : Results.Json(new { code = "step_up_required" }, statusCode: 403));
    app.MapPost("/__tests/dispatcher-command", (IAccessContextAccessor access) => access.Current is null ? Results.Json(new { code = "not_signed_in" }, statusCode: 401) : Results.Ok(new { actor = access.Current.EmployeeId }));
}
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi("/openapi/{documentName}.json");
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
public sealed record SetVehicleOffRoadRequest(int Version, bool ManualOffRoad, string? Reason);
public sealed record DocumentUploadRequest(DocumentOwnerType OwnerType, Guid OwnerId, DocumentType Type, string ContentType, long SizeBytes);
public sealed record DocumentCreateRequest(DocumentOwnerType OwnerType, Guid OwnerId, DocumentType Type, string? Number, DateOnly? IssuedAt, DateOnly? ExpiresAt, string[]? Categories, IReadOnlyList<ConfirmFileInput> Files);
public sealed record DocumentVerifyRequest(int Version, DateOnly? ExpiresAt = null, DateOnly? IssuedAt = null, string? Number = null, string[]? Categories = null);
public sealed record DocumentRejectRequest(int Version, string Reason);
public sealed record DocumentDeleteRequest(int Version, string Reason);
public sealed record DocumentSightCheckRequest(int Version, DateOnly CheckedAt);
public sealed record SnoozeAlertRequest(int Version, DateTimeOffset Until, string Reason);
public sealed record UnsnoozeAlertRequest(int Version);
public sealed class VehicleQueryHandler(IConfiguration configuration, IReadiness readiness, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<GetVehicleQuery, VehicleDetail>
{ public async Task<VehicleDetail> HandleAsync(GetVehicleQuery query, AccessContext actor, CancellationToken cancellationToken) => await (await EfVehicleStore.ReadByConnectionAsync(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured."), query.SubjectId ?? throw new VehicleValidationException(["id"]), cancellationToken)).ComposeAsync(readiness, companyTime, time, cancellationToken); }

public sealed class VehicleListQueryHandler(IConfiguration configuration, IDataProtectionProvider protectionProvider, IReadiness readiness, ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<ListVehiclesQuery, VehicleListResult>
{
    private readonly IDataProtector protector = protectionProvider.CreateProtector("FleetOps.VehicleListCursor.v1");

    public async Task<VehicleListResult> HandleAsync(ListVehiclesQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var cursor = Unprotect(query.Cursor);
        var page = await EfVehicleStore.ReadListByConnectionAsync(DatabaseConnectionString.Resolve(configuration) ?? throw new InvalidOperationException("Database is not configured."), query, cursor, cancellationToken);
        var nextCursor = page.NextCursor is null ? null : protector.Protect(JsonSerializer.Serialize(page.NextCursor));
        return await new VehicleListResult(page.Items, nextCursor).ComposeAsync(readiness, companyTime, time, cancellationToken);
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
