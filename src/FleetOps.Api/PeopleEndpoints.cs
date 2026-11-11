using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.People;

namespace FleetOps.Api;

public static class PeopleEndpoints
{
    public static void AddPeople(this IServiceCollection services)
    {
        services.AddScoped<IPeopleStore, FleetOps.Migrations.EfPeopleStore>();
        services.AddScoped<ICommandHandler<ApplyScheduledLeaveCommand, PersonSaveResult>, ApplyScheduledLeaveHandler>();
        services.AddScoped<ILeaveStore, FleetOps.Migrations.EfLeaveStore>();
        services.AddScoped<IEmployeeLeftStore, FleetOps.Migrations.EfLeaveStore>();
        services.AddScoped<IEmployeeLeft, EmployeeLeftHandler>();
        services.AddScoped<ICommandHandler<LeavePersonCommand, PersonSaveResult>, LeavePersonHandler>();
        services.AddScoped<IQueryHandler<GetLeavePreviewQuery, LeavePreview>, GetLeavePreviewHandler>();
        services.AddScoped<ICommandHandler<SuspendPersonCommand, PersonSaveResult>, SuspendPersonHandler>();
        services.AddScoped<ICommandHandler<ReactivatePersonCommand, PersonSaveResult>, ReactivatePersonHandler>();
        services.AddScoped<ICommandHandler<RehirePersonCommand, PersonSaveResult>, RehirePersonHandler>();
        services.AddScoped<IPersonEnrolment, PersonEnrolment>();
        services.AddScoped<ICommandHandler<CreatePersonCommand, PersonSaveResult>, CreatePersonHandler>();
        services.AddScoped<ICommandHandler<EditPersonCommand, PersonSaveResult>, EditPersonHandler>();
        services.AddScoped<ICommandHandler<ActivatePersonCommand, PersonSaveResult>, ActivatePersonHandler>();
        services.AddScoped<IQueryHandler<ListPeopleQuery, PeoplePage>, ListPeopleHandler>();
        services.AddScoped<IQueryHandler<GetPersonQuery, PersonDetail>, GetPersonHandler>();
    }

    public static void MapPeople(this WebApplication app)
    {
        app.MapGet("/api/v1/people/{id:guid}/leave-preview", async (Guid id, DateOnly? endDate, QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<GetLeavePreviewQuery, LeavePreview>(new(id, endDate), cancellationToken))).Produces<LeavePreview>();
        app.MapPost("/api/v1/people/{id:guid}/leave", async (Guid id, LeavePersonRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<LeavePersonCommand, PersonSaveResult>(new(id, request.EndDate, request.Reason, request.RunningShiftEnd, request.RunningShiftReason), cancellationToken))).Produces<PersonSaveResult>();
        app.MapPost("/api/v1/people/{id:guid}/rehire", async (Guid id, PersonReasonRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<RehirePersonCommand, PersonSaveResult>(new(id, request.Reason), cancellationToken))).Produces<PersonSaveResult>();
        app.MapPost("/api/v1/people/{id:guid}/reactivate", async (Guid id, PersonReasonRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<ReactivatePersonCommand, PersonSaveResult>(new(id, request.Reason), cancellationToken))).Produces<PersonSaveResult>();
        app.MapPost("/api/v1/people/{id:guid}/suspend", async (Guid id, PersonReasonRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<SuspendPersonCommand, PersonSaveResult>(new(id, request.Reason), cancellationToken))).Produces<PersonSaveResult>();
        app.MapPost("/api/v1/people", async (CreatePersonRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var result = await dispatcher.DispatchAsync<CreatePersonCommand, PersonSaveResult>(new(request.Fields(), request.Roles), cancellationToken);
            return Results.Created($"/api/v1/people/{result.Id}", result);
        }).Produces<PersonSaveResult>(StatusCodes.Status201Created);
        app.MapPut("/api/v1/people/{id:guid}", async (Guid id, EditPersonRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<EditPersonCommand, PersonSaveResult>(new(id, request.Fields()), cancellationToken))).Produces<PersonSaveResult>();
        app.MapGet("/api/v1/people", async (int? page, int? pageSize, string? role, string? status, bool? blockedOnly,
            bool? expiringWithin30, string? search, QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<ListPeopleQuery, PeoplePage>(new(page ?? 1, pageSize ?? 50, role, status,
                blockedOnly == true, expiringWithin30 == true, search), cancellationToken))).Produces<PeoplePage>();
        app.MapGet("/api/v1/people/{id:guid}", async (Guid id, QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<GetPersonQuery, PersonDetail>(new(id), cancellationToken))).Produces<PersonDetail>();
        app.MapPost("/api/v1/people/{id:guid}/activate", async (Guid id, ActivatePersonRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<ActivatePersonCommand, PersonSaveResult>(new(id, request.AcceptWarning), cancellationToken))).Produces<PersonSaveResult>();
    }
}
