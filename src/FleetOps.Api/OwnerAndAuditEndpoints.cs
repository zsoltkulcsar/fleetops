using System.Text;
using FleetOps.Api.Pipeline;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Notifications;
namespace FleetOps.Api;
public static class OwnerAndAuditEndpoints
{
    public static void MapOwnerAndAudit(this WebApplication app)
    {
        app.MapGet("/api/v1/owner/overview", async (QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await dispatcher.DispatchAsync<GetOwnerOverviewQuery, OwnerOverview>(new(), cancellationToken));
        }).Produces<OwnerOverview>();
        app.MapGet("/api/v1/audit-log", async ([AsParameters] AuditFilter filter, QueryDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await dispatcher.DispatchAsync<GetAuditLogQuery, AuditPage>(new(filter), cancellationToken));
        }).Produces<AuditPage>();
        app.MapGet("/api/v1/audit-log/export", async ([AsParameters] AuditFilter filter, CommandDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var csv = await dispatcher.DispatchAsync<ExportAuditLogCommand, string>(new(filter), cancellationToken);
            return Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "audit-log.csv");
        });
    }
}
