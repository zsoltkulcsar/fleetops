using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
namespace FleetOps.Api;
public static class AbsenceEndpoints
{
    public static void AddAbsences(this IServiceCollection services)
    {
        services.AddScoped<ICommandHandler<AcknowledgeAbsenceCommand, ReleaseReviewResult>, AcknowledgeAbsenceHandler>();
        services.AddScoped<IAbsenceStore, EfAbsenceStore>();
        services.AddScoped<IDriverAbsences, DriverAbsences>();
        services.AddScoped<IDriverPlanningCommands, DriverPlanningCommands>();
        services.AddScoped<ICommandHandler<RecordAbsenceCommand, AbsenceView>, AbsenceCommands>();
        services.AddScoped<ICommandHandler<DecideAbsenceCommand, AbsenceView>, AbsenceCommands>();
        services.AddScoped<ICommandHandler<CancelAbsenceCommand, AbsenceView>, AbsenceCommands>();
        services.AddScoped<ICommandHandler<EditAbsenceCommand, AbsenceView>, AbsenceCommands>();
        services.AddScoped<IQueryHandler<ListAbsencesQuery, AbsenceList>, AbsenceQueries>();
        services.AddScoped<IQueryHandler<GetAbsenceQuery, AbsenceDetail>, AbsenceQueries>();
        services.AddScoped<IScheduleRequestStore, EfScheduleRequestStore>();
        services.AddScoped<IScheduleRequests, ScheduleRequests>();
        services.AddScoped<ICommandHandler<DecideScheduleRequestCommand, ScheduleRequestView>, ScheduleRequests>();
        services.AddScoped<IQueryHandler<ListScheduleRequestsQuery, ScheduleRequestList>, ScheduleRequests>();
        services.AddScoped<IPlanningAlertFacts, EfPlanningAlertFacts>();
        services.AddScoped<IAlertEvaluator, A18AbsenceWorkedEvaluator>();
        services.AddScoped<IAlertEvaluator, A19ScheduleRequestEvaluator>();
    }
    public static void MapAbsences(this WebApplication app)
    {
        app.MapPost("/api/v1/alerts/{id:guid}/acknowledge-absence", async (Guid id, AbsenceAcknowledgementRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<AcknowledgeAbsenceCommand, ReleaseReviewResult>(new(id, request.Version), cancellationToken))).Produces<ReleaseReviewResult>();
        app.MapGet("/api/v1/absences", async (DateOnly from, DateOnly to, Guid? employeeId, QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<ListAbsencesQuery, AbsenceList>(new(from, to, employeeId), cancellationToken))).Produces<AbsenceList>();
        app.MapGet("/api/v1/absences/{id:guid}", async (Guid id, QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<GetAbsenceQuery, AbsenceDetail>(new(id), cancellationToken))).Produces<AbsenceDetail>();
        app.MapPost("/api/v1/absences", async (RecordAbsenceCommand request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<RecordAbsenceCommand, AbsenceView>(request, cancellationToken))).Produces<AbsenceView>();
        app.MapPost("/api/v1/absences/{id:guid}/decide", async (Guid id, AbsenceDecisionRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<DecideAbsenceCommand, AbsenceView>(new(id, request.Approve, request.Reason, request.ConfirmOverlap), cancellationToken))).Produces<AbsenceView>();
        app.MapPost("/api/v1/absences/{id:guid}/cancel", async (Guid id, AbsenceCancellationRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<CancelAbsenceCommand, AbsenceView>(new(id, request.Reason), cancellationToken))).Produces<AbsenceView>();
        app.MapPut("/api/v1/absences/{id:guid}", async (Guid id, AbsenceEditRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<EditAbsenceCommand, AbsenceView>(new(id, request.From, request.To, request.HalfDay, request.Type, request.Reason, request.ConfirmOverlap), cancellationToken))).Produces<AbsenceView>();
        app.MapGet("/api/v1/schedule/requests", async (QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<ListScheduleRequestsQuery, ScheduleRequestList>(new(), cancellationToken))).Produces<ScheduleRequestList>();
        app.MapPost("/api/v1/schedule/requests/{id:guid}/decide", async (Guid id, ScheduleRequestDecisionRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<DecideScheduleRequestCommand, ScheduleRequestView>(new(id, request.Accept, request.Reason), cancellationToken))).Produces<ScheduleRequestView>();
    }
}
