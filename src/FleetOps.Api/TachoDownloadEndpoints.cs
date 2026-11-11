using FleetOps.Api.Pipeline;
using FleetOps.Modules.Fleet;
using FleetOps.People;
namespace FleetOps.Api;

public static class TachoDownloadEndpoints
{
    public static void MapTachoDownloads(this WebApplication app)
    {
        app.MapPost("/api/v1/fleet/vehicles/{id:guid}/tacho-downloads", async (Guid id, LogTachoDownloadRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<LogTruckTachoDownloadCommand, TruckTachoDownloadResult>(new(id, request.Date), cancellationToken))).Produces<TruckTachoDownloadResult>();
        app.MapPost("/api/v1/people/{id:guid}/tacho-downloads", async (Guid id, LogTachoDownloadRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken)
            => Results.Ok(await dispatcher.DispatchAsync<LogPersonTachoDownloadCommand, PersonTachoDownloadResult>(new(id, request.Date), cancellationToken))).Produces<PersonTachoDownloadResult>();
    }
}
