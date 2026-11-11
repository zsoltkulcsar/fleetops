using FleetOps.Api.Pipeline;
using FleetOps.Modules.Maintenance;
namespace FleetOps.Api;

public static class RepairEndpoints
{
    public static void MapRepairs(this WebApplication app)
    {
        app.MapPost("/api/v1/fleet/maintenance", async (CreateRepairCommand command, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<CreateRepairCommand, RepairDetail>(command, cancellationToken))).Produces<RepairDetail>();
        app.MapPost("/api/v1/fleet/maintenance/{id:guid}/start", async (Guid id, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<StartRepairCommand, RepairDetail>(new(id), cancellationToken))).Produces<RepairDetail>();
        app.MapPost("/api/v1/fleet/maintenance/{id:guid}/done", async (Guid id, DoneRepairRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<DoneRepairCommand, RepairDetail>(new(id, request.DoneAt, request.OdometerAtDoneKm), cancellationToken))).Produces<RepairDetail>();
        app.MapPost("/api/v1/fleet/maintenance/{id:guid}/cancel", async (Guid id, CancelRepairRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<CancelRepairCommand, RepairDetail>(new(id, request.Reason), cancellationToken))).Produces<RepairDetail>();
        app.MapGet("/api/v1/fleet/maintenance", async (Guid? vehicleId, MaintenanceStatus? status, DateOnly? from, DateOnly? to, QueryDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<ListMaintenanceQuery, MaintenanceList>(new(vehicleId, status, from, to), cancellationToken))).Produces<MaintenanceList>();
        app.MapGet("/api/v1/fleet/vehicles/{id:guid}/maintenance", async (Guid id, MaintenanceStatus? status, DateOnly? from, DateOnly? to, QueryDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<ListMaintenanceQuery, MaintenanceList>(new(id, status, from, to), cancellationToken))).Produces<MaintenanceList>();
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
