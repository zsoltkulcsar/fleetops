using FleetOps.Api.Auth;
using FleetOps.Api.Pipeline;
using FleetOps.Identity;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Api;
public static class PersonAccessEndpoints
{
    public static void AddPersonAccess(this IServiceCollection services)
    {
        services.AddScoped<IPersonAccessStore, FleetOps.Migrations.EfPersonAccessStore>();
        services.AddScoped<ICurrentSession>(provider => provider.GetRequiredService<SessionAccessor>());
        services.AddScoped<IOfficeInvitationMail, OfficeInvitationMail>();
        services.AddScoped<IQueryHandler<GetPersonSessionsQuery, PersonSessions>, PersonAccessHandler>();
        services.AddScoped<ICommandHandler<RevokePersonSessionCommand, AccessActionResult>, PersonAccessHandler>();
        services.AddScoped<ICommandHandler<ResetPersonPinCommand, AccessActionResult>, PersonAccessHandler>();
        services.AddScoped<ICommandHandler<CompletePinResetCommand, AccessActionResult>, PersonAccessHandler>();
        services.AddScoped<ICommandHandler<ChangePersonPhoneCommand, PhoneChangeResult>, PersonAccessHandler>();
        services.AddScoped<ICommandHandler<SetPersonRolesCommand, AccessActionResult>, PersonAccessHandler>();
        services.AddScoped<ICommandHandler<InviteOfficePersonCommand, AccessActionResult>, PersonAccessHandler>();
    }
    public static void MapPersonAccess(this WebApplication app)
    {
        app.MapGet("/api/v1/people/{id:guid}/sessions", async (Guid id, QueryDispatcher queries, CancellationToken cancellationToken) =>
            Results.Ok(await queries.DispatchAsync<GetPersonSessionsQuery, PersonSessions>(new(id), cancellationToken))).Produces<PersonSessions>();
        app.MapPost("/api/v1/people/{id:guid}/sessions/{sessionId:guid}/revoke", async (Guid id, Guid sessionId, ReasonRequest request, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<RevokePersonSessionCommand, AccessActionResult>(new(id, sessionId, request.Reason), cancellationToken))).Produces<AccessActionResult>();
        app.MapPost("/api/v1/people/{id:guid}/pin-reset", async (Guid id, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<ResetPersonPinCommand, AccessActionResult>(new(id), cancellationToken))).Produces<AccessActionResult>();
        app.MapPut("/api/v1/people/{id:guid}/phone", async (Guid id, PhoneRequest request, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<ChangePersonPhoneCommand, PhoneChangeResult>(new(id, request.Phone, request.Reason), cancellationToken))).Produces<PhoneChangeResult>();
        app.MapPost("/api/v1/driver/pin-reset/complete", async (IAccessContextAccessor access, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<CompletePinResetCommand, AccessActionResult>(new(access.Current?.EmployeeId), cancellationToken))).Produces<AccessActionResult>();
        app.MapPut("/api/v1/people/{id:guid}/roles", async (Guid id, RolesRequest request, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<SetPersonRolesCommand, AccessActionResult>(new(id, request.Roles, request.Reason), cancellationToken))).Produces<AccessActionResult>();
        app.MapPost("/api/v1/people/{id:guid}/invite", async (Guid id, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<InviteOfficePersonCommand, AccessActionResult>(new(id), cancellationToken))).Produces<AccessActionResult>();
    }
    public sealed record ReasonRequest(string? Reason);
    public sealed record PhoneRequest(string? Phone, string? Reason);
    public sealed record RolesRequest(string[]? Roles, string? Reason);
}
