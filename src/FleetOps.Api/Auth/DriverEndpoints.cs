using FleetOps.Api.Pipeline;
using FleetOps.Identity;
using FleetOps.Kernel.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace FleetOps.Api.Auth;
public static class DriverEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/api/v1/people/{id:guid}/enrolment", async (Guid id, CommandDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(await dispatcher.DispatchAsync<SendEnrolmentCommand, SendEnrolmentResult>(new(id), ct)))
            .Produces<SendEnrolmentResult>().ProducesProblem(409).ProducesProblem(503);
        app.MapPost("/api/v1/auth/driver/enrol", async ([FromBody] EnrolDriverCommand input, HttpContext http, CommandDispatcher dispatcher, CancellationToken ct) =>
        {
            var result = await dispatcher.DispatchAsync(input, ct);
            if (result is null)
                return Results.Json(new { code = "enrolment_failed" }, statusCode: 401);
            AuthEndpoints.SetCookie(http, result.Token, TimeSpan.FromDays(result.SessionDays));
            return Results.Ok(new DriverEnrolmentResponse(result.Stage));
        }).Produces<DriverEnrolmentResponse>().Produces(401);
        app.MapGet("/api/v1/driver/config", async (QueryDispatcher dispatcher, IAccessContextAccessor accessor, CancellationToken ct) =>
            Results.Ok(await dispatcher.DispatchAsync<GetDriverConfigQuery, DriverConfig>(new(accessor.Current?.EmployeeId), ct)))
            .Produces<DriverConfig>();
    }
}
