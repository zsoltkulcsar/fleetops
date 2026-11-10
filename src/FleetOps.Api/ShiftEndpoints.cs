using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Shifts;

namespace FleetOps.Api;

public sealed record DriverCommandBatch(IReadOnlyList<DriverCommand> Commands);
public sealed record DriverCommandBatchResult(IReadOnlyList<DriverCommandResult> Results);

public static class ShiftEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/v1/driver/shifts", async (IAccessContextAccessor access, QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await dispatcher.DispatchAsync<GetDriverShiftsQuery, ShiftSnapshot>(new(access.Current?.EmployeeId), cancellationToken));
        }).Produces<ShiftSnapshot>();
        app.MapPost("/api/v1/driver/commands/batch", async (DriverCommandBatch request, IAccessContextAccessor access, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            if (request.Commands is null || request.Commands.Count is < 1 or > 20)
                return Results.BadRequest(new { code = "validation_error", fields = new[] { "commands" } });
            var results = new List<DriverCommandResult>();
            foreach (var command in request.Commands)
            {
                try
                {
                    results.Add(await dispatcher.DispatchAsync<ApplyDriverCommand, DriverCommandResult>(new(access.Current?.EmployeeId, command), cancellationToken));
                }
                catch (ShiftRuleException failure)
                {
                    results.Add(new(command?.CommandId ?? Guid.Empty, "failed", failure.Code, null));
                }
            }
            return Results.Ok(new DriverCommandBatchResult(results));
        }).Produces<DriverCommandBatchResult>();
    }
}
