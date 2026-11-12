using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Notifications;
namespace FleetOps.Api;
public sealed record PushSubscriptionRequest(string Endpoint, string P256dh, string Auth);
public sealed record DeletePushSubscriptionRequest(string Endpoint);
public sealed record AlertCoverRequest(Guid ToId, DateOnly From, DateOnly To);
public static class NotificationSettingsEndpoints
{
    public static void MapNotificationSettings(this WebApplication app)
    {
        var routes = app.MapGroup("/api/v1/me");
        routes.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        routes.MapGet("/notify-prefs", async (QueryDispatcher dispatcher, IAccessContextAccessor access, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<GetNotifyPreferencesQuery, NotifyPreferences>(new(access.Current?.EmployeeId), cancellationToken))).Produces<NotifyPreferences>();
        routes.MapPut("/notify-prefs", async (NotifyPreferences preferences, CommandDispatcher dispatcher, IAccessContextAccessor access, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<SaveNotifyPreferencesCommand, NotifyPreferences>(new(preferences, access.Current?.EmployeeId), cancellationToken))).Produces<NotifyPreferences>();
        routes.MapGet("/push", async (QueryDispatcher dispatcher, IAccessContextAccessor access, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<GetPushStatusQuery, PushStatus>(new(access.Current?.EmployeeId), cancellationToken))).Produces<PushStatus>();
        routes.MapPost("/push-subscriptions", async (PushSubscriptionRequest request, CommandDispatcher dispatcher, IAccessContextAccessor access, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<SavePushSubscriptionCommand, bool>(new(request.Endpoint, request.P256dh, request.Auth, access.Current?.EmployeeId), cancellationToken)));
        routes.MapDelete("/push-subscriptions", async ([Microsoft.AspNetCore.Mvc.FromBody] DeletePushSubscriptionRequest request, CommandDispatcher dispatcher, IAccessContextAccessor access, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<DeletePushSubscriptionCommand, bool>(new(request.Endpoint, access.Current?.EmployeeId), cancellationToken)));
        routes.MapGet("/alert-covers", async (QueryDispatcher dispatcher, IAccessContextAccessor access, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<GetAlertCoversQuery, AlertCoverList>(new(access.Current?.EmployeeId), cancellationToken))).Produces<AlertCoverList>();
        routes.MapPost("/alert-covers", async (AlertCoverRequest request, CommandDispatcher dispatcher, IAccessContextAccessor access, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<SaveAlertCoverCommand, AlertCoverList>(new(null, request.ToId, request.From, request.To, access.Current?.EmployeeId), cancellationToken))).Produces<AlertCoverList>();
        routes.MapPut("/alert-covers/{id:guid}", async (Guid id, AlertCoverRequest request, CommandDispatcher dispatcher, IAccessContextAccessor access, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<SaveAlertCoverCommand, AlertCoverList>(new(id, request.ToId, request.From, request.To, access.Current?.EmployeeId), cancellationToken))).Produces<AlertCoverList>();
        routes.MapDelete("/alert-covers/{id:guid}", async (Guid id, CommandDispatcher dispatcher, IAccessContextAccessor access, CancellationToken cancellationToken) =>
            await dispatcher.DispatchAsync<DeleteAlertCoverCommand, bool>(new(id, access.Current?.EmployeeId), cancellationToken) ? Results.Ok() : Results.NotFound());
    }
}
