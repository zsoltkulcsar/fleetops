using FleetOps.Api.Pipeline;
using FleetOps.Modules.Payroll;

namespace FleetOps.Api;

public static class PayrollEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/v1/payroll");
        group.MapGet("/readiness", async (DateOnly month, QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<GetPayrollReadinessQuery, PayrollReadiness>(new(month), cancellationToken))).Produces<PayrollReadiness>();
        group.MapGet("/preview", async (DateOnly month, QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<PreviewPayrollQuery, PayrollPreview>(new(month), cancellationToken))).Produces<PayrollPreview>();
        group.MapGet("/exports", async (DateOnly month, QueryDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<GetPayrollHistoryQuery, PayrollHistory>(new(month), cancellationToken))).Produces<PayrollHistory>();
        group.MapPost("/exports", async (GeneratePayrollCommand request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<GeneratePayrollCommand, PayrollExportView>(request, cancellationToken))).Produces<PayrollExportView>();
        group.MapPost("/exports/{id:guid}/sent", async (Guid id, PayrollSentRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<MarkPayrollSentCommand, PayrollExportView>(new(id, request.SentDate, request.Note), cancellationToken))).Produces<PayrollExportView>();
        group.MapPost("/exports/{id:guid}/discard", async (Guid id, PayrollReasonRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<DiscardPayrollCommand, PayrollExportView>(new(id, request.Reason), cancellationToken))).Produces<PayrollExportView>();
        group.MapPost("/exports/{id:guid}/download-link", async (Guid id, CommandDispatcher dispatcher, HttpContext context, CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(await dispatcher.DispatchAsync<DownloadPayrollCommand, PayrollDownload>(new(id), cancellationToken));
        }).Produces<PayrollDownload>();
        app.MapPost("/api/v1/shifts/{id:guid}/reopen", async (Guid id, PayrollReopenRequest request, CommandDispatcher dispatcher, CancellationToken cancellationToken) =>
            Results.Ok(await dispatcher.DispatchAsync<ReopenPayrollShiftCommand, PayrollReopened>(new(id, request.Version, request.Reason), cancellationToken))).Produces<PayrollReopened>();
    }
}

public sealed record PayrollSentRequest(DateOnly SentDate, string? Note);
public sealed record PayrollReasonRequest(string Reason);
public sealed record PayrollReopenRequest(int Version, string Reason);
