using FleetOps.Api.Pipeline;
using FleetOps.Modules.Maintenance;
using FleetOps.Modules.Documents;
namespace FleetOps.Api;

public static class RepairEndpoints
{
    public static void MapRepairs(this WebApplication app)
    {
        app.MapPost("/api/v1/fleet/maintenance/documents/upload", async (RequestMaintenanceDocumentUploadCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<RequestMaintenanceDocumentUploadCommand, UploadLinkResult>(command, cancellationToken))).Produces<UploadLinkResult>();
        app.MapPost("/api/v1/fleet/maintenance/invoices/confirm", async (ConfirmMaintenanceInvoiceCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<ConfirmMaintenanceInvoiceCommand, DocumentDetail>(command, cancellationToken))).Produces<DocumentDetail>();
        app.MapPost("/api/v1/fleet/maintenance", async (CreateRepairCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<CreateRepairCommand, RepairDetail>(command, cancellationToken))).Produces<RepairDetail>();
        app.MapPut("/api/v1/fleet/maintenance/{id:guid}", async (Guid id, RescheduleMaintenanceRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<RescheduleMaintenanceCommand, RepairDetail>(new(id, request.ScheduledFor, request.Workshop, request.DueDate, request.DueKm), cancellationToken))).Produces<RepairDetail>();
        app.MapPost("/api/v1/fleet/maintenance/{id:guid}/start", async (Guid id, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<StartRepairCommand, RepairDetail>(new(id), cancellationToken))).Produces<RepairDetail>();
        app.MapPost("/api/v1/fleet/maintenance/{id:guid}/done", async (Guid id, DoneRepairRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<DoneRepairCommand, RepairDetail>(new(id, request.DoneAt, request.OdometerAtDoneKm, request.CostEur, request.InvoiceDocumentId, request.ResultDocument), cancellationToken))).Produces<RepairDetail>();
        app.MapPost("/api/v1/fleet/maintenance/{id:guid}/cancel", async (Guid id, CancelRepairRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<CancelRepairCommand, RepairDetail>(new(id, request.Reason), cancellationToken))).Produces<RepairDetail>();
        app.MapGet("/api/v1/fleet/maintenance", async (Guid? vehicleId, string? status, DateOnly? from, DateOnly? to, string? type, bool? overdue, QueryDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<ListMaintenanceQuery, MaintenanceList>(new(vehicleId, null, from, to, type, overdue, status), cancellationToken))).Produces<MaintenanceList>();
        app.MapGet("/api/v1/fleet/vehicles/{id:guid}/maintenance", async (Guid id, string? status, DateOnly? from, DateOnly? to, string? type, bool? overdue, QueryDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<ListMaintenanceQuery, MaintenanceList>(new(id, null, from, to, type, overdue, status), cancellationToken))).Produces<MaintenanceList>();
        app.MapGet("/api/v1/fleet/swap-options", async (Guid employeeId, DateOnly date, QueryDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<SwapOptionsQuery, SwapOptions>(new(employeeId, date), cancellationToken))).Produces<SwapOptions>();
        app.MapPost("/api/v1/fleet/assignments", async (CreateAssignmentCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<CreateAssignmentCommand, AssignmentDetail>(command, cancellationToken))).Produces<AssignmentDetail>();
        app.MapPut("/api/v1/fleet/assignments/{id:guid}", async (Guid id, EditAssignmentRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<EditAssignmentCommand, AssignmentDetail>(new(id, request.From, request.To), cancellationToken))).Produces<AssignmentDetail>();
        app.MapPost("/api/v1/fleet/assignments/{id:guid}/end", async (Guid id, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<EndAssignmentCommand, AssignmentDetail>(new(id), cancellationToken))).Produces<AssignmentDetail>();
    }
}
