using FleetOps.Api.Pipeline;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
namespace FleetOps.Api;

public static class OfficeDefectEndpoints
{
    public static void MapOfficeDefects(this WebApplication app)
    {
        app.MapGet("/api/v1/fleet/defects", async (int? page, int? pageSize, string? statusGroup, DefectSeverity? severity, Guid? vehicleId, QueryDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<ListDefectsQuery, DefectListResult>(new(page ?? 1, pageSize ?? 50, statusGroup, severity, vehicleId), cancellationToken))).Produces<DefectListResult>();
        app.MapGet("/api/v1/fleet/defects/{id:guid}", async (Guid id, QueryDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<GetDefectQuery, DefectDetail>(new(id), cancellationToken))).Produces<DefectDetail>();
        app.MapGet("/api/v1/fleet/vehicles/{id:guid}/defects", async (Guid id, int? page, int? pageSize, string? statusGroup, DefectSeverity? severity, QueryDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<ListDefectsQuery, DefectListResult>(new(page ?? 1, pageSize ?? 50, statusGroup, severity, id), cancellationToken))).Produces<DefectListResult>();
        app.MapPost("/api/v1/fleet/defects", async (LogOfficeDefectCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<LogOfficeDefectCommand, DefectActionResult>(command, cancellationToken))).Produces<DefectActionResult>();
        app.MapPost("/api/v1/fleet/defects/{id:guid}/confirm", async (Guid id, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<ConfirmDefectCommand, DefectActionResult>(new(id), cancellationToken))).Produces<DefectActionResult>();
        app.MapPost("/api/v1/fleet/defects/{id:guid}/downgrade", async (Guid id, DowngradeDefectRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<DowngradeDefectCommand, DefectActionResult>(new(id, request.Severity, request.Reason), cancellationToken))).Produces<DefectActionResult>();
        app.MapPost("/api/v1/fleet/defects/{id:guid}/close", async (Guid id, CloseDefectRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<CloseDefectCommand, DefectActionResult>(new(id, request.Reason), cancellationToken))).Produces<DefectActionResult>();
        app.MapPost("/api/v1/fleet/defects/upload-links", async (RequestOfficeDefectPhotoUploadCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<RequestOfficeDefectPhotoUploadCommand, UploadLinkResult>(command, cancellationToken))).Produces<UploadLinkResult>();
    }
}
