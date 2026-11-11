using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.People;

namespace FleetOps.Api;

public static class PeopleEndpoints
{
    public static void AddPeople(this IServiceCollection services)
    {
        services.AddScoped<IPeopleStore, FleetOps.Migrations.EfPeopleStore>();
        services.AddScoped<IPersonEnrolment, PersonEnrolment>();
        services.AddScoped<ICommandHandler<CreatePersonCommand, PersonSaveResult>, CreatePersonHandler>();
        services.AddScoped<ICommandHandler<EditPersonCommand, PersonSaveResult>, EditPersonHandler>();
        services.AddScoped<ICommandHandler<ActivatePersonCommand, PersonSaveResult>, ActivatePersonHandler>();
        services.AddScoped<IQueryHandler<ListPeopleQuery, PeoplePage>, ListPeopleHandler>();
        services.AddScoped<IQueryHandler<GetPersonQuery, PersonDetail>, GetPersonHandler>();
    }

    public static void MapPeople(this WebApplication app)
    {
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
