using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Fleet;
namespace FleetOps.Api;
public static class VehicleReleaseEndpoints
{
    public static void AddVehicleRelease(this IServiceCollection services)
    {
        services.AddScoped<IVehicleReleaseStore, EfVehicleReleaseStore>();
        services.AddScoped<IVehicleReleaseReview, OpenReleaseReviewHandler>();
        services.AddScoped<IReleaseReviewStore, EfReleaseReviewStore>();
        services.AddScoped<ICommandHandler<ReleaseVehicleCommand, VehicleReleaseResult>, ReleaseVehicleHandler>();
        services.AddScoped<ICommandHandler<AcknowledgeReleaseCommand, ReleaseReviewResult>, AcknowledgeReleaseHandler>();
    }
    public static void MapVehicleRelease(this WebApplication app)
    {
        app.MapPost("/api/v1/fleet/vehicles/{id:guid}/release", async (Guid id, ReleaseRequest request, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<ReleaseVehicleCommand, VehicleReleaseResult>(new(id, request.Reason, request.ValidUntil), cancellationToken))).Produces<VehicleReleaseResult>();
        app.MapPost("/api/v1/alerts/{id:guid}/acknowledge", async (Guid id, ReviewRequest request, CommandDispatcher commands, CancellationToken cancellationToken) =>
            Results.Ok(await commands.DispatchAsync<AcknowledgeReleaseCommand, ReleaseReviewResult>(new(id, request.Version), cancellationToken))).Produces<ReleaseReviewResult>();
    }
    public sealed record ReleaseRequest(string? Reason, DateTimeOffset? ValidUntil = null);
    public sealed record ReviewRequest(int Version);
}
