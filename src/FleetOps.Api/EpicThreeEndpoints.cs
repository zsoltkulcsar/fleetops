using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Views;

namespace FleetOps.Api;

public sealed record ManualCycleRequest(Guid EmployeeId, Guid VehicleId, DateOnly From, DateOnly To, string Reason);
public sealed record EditCycleRequest(int Version, Guid EmployeeId, Guid VehicleId, DateOnly From, DateOnly To, string Reason);
public sealed record DriverUploadLinkRequest(DocumentType Type, string ContentType, long SizeBytes);
public sealed record DriverDocumentCreateRequest(DocumentType Type, string? Number, DateOnly? IssuedAt, DateOnly? ExpiresAt, string[]? Categories, IReadOnlyList<ConfirmFileInput> Files);

public static class EpicThreeEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/schedule", async (DateOnly from, DateOnly to, Guid? employeeId, Guid? vehicleId, QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await dispatcher.DispatchAsync<GetScheduleQuery, ScheduleResult>(new(from, to, employeeId, vehicleId), cancellationToken));
        }).Produces<ScheduleResult>();
        app.MapGet("/api/v1/people/{id:guid}/cycle-pattern", async (Guid id, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetCyclePatternQuery, CyclePatternView>(new(id), cancellationToken))).Produces<CyclePatternView>();
        app.MapPost("/api/v1/schedule/cycles", async (ManualCycleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<AddManualCycleCommand, CycleDetail>(new(request.EmployeeId, request.VehicleId, request.From, request.To, request.Reason), cancellationToken))).Produces<CycleDetail>();
        app.MapPut("/api/v1/schedule/cycles/{id:guid}", async (Guid id, EditCycleRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<EditCycleCommand, CycleDetail>(new(id, request.Version, request.EmployeeId, request.VehicleId, request.From, request.To, request.Reason), cancellationToken))).Produces<CycleDetail>();
        app.MapGet("/api/v1/fleet/vehicles/{id:guid}/retirement-impact", async (Guid id, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetVehicleRetirementImpactQuery, VehicleRetirementImpact>(new(id), cancellationToken))).Produces<VehicleRetirementImpact>();
        app.MapGet("/api/v1/driver/snapshot", async (IAccessContextAccessor access, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetDriverSnapshotQuery, DriverSnapshot>(new(access.Current?.EmployeeId), cancellationToken))).Produces<DriverSnapshot>();
        app.MapGet("/api/v1/driver/vehicles/{id:guid}", async (Guid id, IAccessContextAccessor access, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetDriverVehicleQuery, DriverVehicleDetail>(new(access.Current?.EmployeeId, id), cancellationToken))).Produces<DriverVehicleDetail>();
        app.MapGet("/api/v1/driver/documents/{id:guid}", async (Guid id, IAccessContextAccessor access, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetDriverDocumentQuery, DriverDocumentDetail>(new(access.Current?.EmployeeId, id), cancellationToken))).Produces<DriverDocumentDetail>();
        app.MapGet("/api/v1/driver/documents/{id:guid}/files/{fileId:guid}/link", async (Guid id, Guid fileId, IAccessContextAccessor access, QueryDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<GetDriverFileLinkQuery, DriverFileLinkResult>(new(access.Current?.EmployeeId, id, fileId), cancellationToken))).Produces<DriverFileLinkResult>();
        app.MapPost("/api/v1/driver/documents/upload-links", async (DriverUploadLinkRequest request, IAccessContextAccessor access, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Ok(await dispatcher.DispatchAsync<RequestEmployeeDocumentUploadCommand, UploadLinkResult>(new(access.Current?.EmployeeId, request.Type, request.ContentType, request.SizeBytes), cancellationToken))).Produces<UploadLinkResult>();
        app.MapPost("/api/v1/driver/documents", async (DriverDocumentCreateRequest request, IAccessContextAccessor access, CommandDispatcher dispatcher, CancellationToken cancellationToken) => Results.Created("/api/v1/driver/documents", await dispatcher.DispatchAsync<ConfirmDriverDocumentCommand, DriverDocumentDetail>(new(access.Current?.EmployeeId, request.Type, request.Number, request.IssuedAt, request.ExpiresAt, request.Categories, request.Files), cancellationToken))).Produces<DriverDocumentDetail>(201);
    }
}
