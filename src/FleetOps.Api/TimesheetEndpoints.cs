using FleetOps.Api.Pipeline;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Shifts;

namespace FleetOps.Api;

public sealed record BulkApprovalItem(Guid ShiftId, int Version, string? Note);
public sealed record BulkApprovalRequest(IReadOnlyList<BulkApprovalItem> Items);
public sealed record BulkApprovalResult(Guid ShiftId, string Status, string? Code);
public sealed record RejectShiftRequest(int Version, string Reason);
public sealed record EditShiftRequest(int Version, string Reason, OfficeShiftInput Input);
public sealed record AddShiftRequest(Guid EmployeeId, string Reason, OfficeShiftInput Input);
public sealed record EndShiftRequest(int Version, string Reason, DateTimeOffset EndAt, int? OdometerEndKm);

public static class TimesheetEndpoints
{
    public static void Map(WebApplication app)
    {
        var routes = app.MapGroup("/api/v1/timesheets");
        routes.MapGet("/week", (DateOnly week, string? filter, string? search, int? page, int? pageSize, QueryDispatcher queries, HttpContext http, CancellationToken cancellationToken) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Run(async () => Results.Ok(await queries.DispatchAsync<GetWeekGridQuery, WeekGrid>(new(week, filter ?? "all", search, page ?? 1, pageSize ?? 50), cancellationToken)));
        });
        routes.MapGet("/shifts/{shiftId:guid}", (Guid shiftId, QueryDispatcher queries, HttpContext http, CancellationToken cancellationToken) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            return Run(async () => Results.Ok(await queries.DispatchAsync<GetShiftDetailQuery, ShiftDetail>(new(shiftId), cancellationToken)));
        });
        routes.MapPost("/approve", async (BulkApprovalRequest request, QueryDispatcher queries, CommandDispatcher commands, IAccessContextAccessor access, CancellationToken cancellationToken) =>
        {
            if (access.Current is null) throw new AuthorizationException(RefusalCodes.NotSignedIn, "shifts.approve_reject", []);
            if (!access.Current.Roles.Any(role => role is RoleCode.Dispatcher or RoleCode.Hr or RoleCode.Owner))
                throw new AuthorizationException(RefusalCodes.PermissionDenied, "shifts.approve_reject", [RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner]);
            if (request.Items is null || request.Items.Count is < 1 or > 1000 || request.Items.Any(item => item is null || item.ShiftId == Guid.Empty || item.Version < 1))
                return Results.BadRequest(new { code = "validation_error", fields = new[] { "items" } });
            var results = new List<BulkApprovalResult>();
            foreach (var item in request.Items)
            {
                try
                {
                    var detail = await queries.DispatchAsync<GetShiftDetailQuery, ShiftDetail>(new(item.ShiftId), cancellationToken);
                    await commands.DispatchAsync<ApproveShiftCommand, OfficeShiftResult>(new(item.ShiftId, detail.EmployeeId, item.Version, item.Note), cancellationToken);
                    results.Add(new(item.ShiftId, "approved", null));
                }
                catch (AuthorizationException failure)
                {
                    var code = failure.Code == RefusalCodes.SelfApproval ? "own_shift" : failure.Code == RefusalCodes.OwnerRoutesToHr ? "owner_shift_needs_hr" : "forbidden";
                    results.Add(new(item.ShiftId, "failed", code));
                }
                catch (ShiftRuleException failure) { results.Add(new(item.ShiftId, "failed", failure.Code)); }
                catch (TimesheetValidationException) { results.Add(new(item.ShiftId, "failed", "validation_error")); }
                catch (System.Data.Common.DbException) { results.Add(new(item.ShiftId, "failed", "save_failed")); }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException) { results.Add(new(item.ShiftId, "failed", "save_failed")); }
            }
            return Results.Ok(new { results, approved = results.Count(result => result.Status == "approved"), failed = results.Count(result => result.Status == "failed") });
        });
        routes.MapPost("/shifts/{shiftId:guid}/reject", (Guid shiftId, RejectShiftRequest request, QueryDispatcher queries, CommandDispatcher commands, CancellationToken cancellationToken) => Run(async () =>
        {
            var detail = await queries.DispatchAsync<GetShiftDetailQuery, ShiftDetail>(new(shiftId), cancellationToken);
            return Results.Ok(await commands.DispatchAsync<RejectShiftCommand, OfficeShiftResult>(new(shiftId, detail.EmployeeId, request.Version, request.Reason), cancellationToken));
        }));
        routes.MapPut("/shifts/{shiftId:guid}", (Guid shiftId, EditShiftRequest request, QueryDispatcher queries, CommandDispatcher commands, CancellationToken cancellationToken) => Run(async () =>
        {
            var detail = await queries.DispatchAsync<GetShiftDetailQuery, ShiftDetail>(new(shiftId), cancellationToken);
            return Results.Ok(await commands.DispatchAsync<EditOfficeShiftCommand, OfficeShiftResult>(new(shiftId, detail.EmployeeId, request.Version, request.Reason, request.Input), cancellationToken));
        }));
        routes.MapPost("/shifts", (AddShiftRequest request, CommandDispatcher commands, CancellationToken cancellationToken) => Run(async () =>
            Results.Ok(await commands.DispatchAsync<AddOfficeShiftCommand, OfficeShiftResult>(new(request.EmployeeId, request.Reason, request.Input), cancellationToken))));
        routes.MapPost("/shifts/{shiftId:guid}/end", (Guid shiftId, EndShiftRequest request, QueryDispatcher queries, CommandDispatcher commands, CancellationToken cancellationToken) => Run(async () =>
        {
            var detail = await queries.DispatchAsync<GetShiftDetailQuery, ShiftDetail>(new(shiftId), cancellationToken);
            return Results.Ok(await commands.DispatchAsync<EndOfficeShiftCommand, OfficeShiftResult>(new(shiftId, detail.EmployeeId, request.Version, request.Reason, request.EndAt, request.OdometerEndKm), cancellationToken));
        }));
    }

    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (TimesheetValidationException failure) { return Results.BadRequest(new { code = "validation_error", fields = failure.Fields }); }
        catch (ShiftRuleException failure)
        {
            if (failure.Code is "not_found" or "shift_not_found") return Results.NotFound(new { code = "not_found" });
            if (failure.Code.StartsWith("invalid_", StringComparison.Ordinal) || failure.Code is "end_before_current_activity" or "vehicle_required_for_driving")
                return Results.BadRequest(new { code = "validation_error", fields = new[] { failure.Code.Contains("overnight", StringComparison.Ordinal) || failure.Code.Contains("country", StringComparison.Ordinal) ? "overnights" : failure.Code.Contains("odometer", StringComparison.Ordinal) ? "odometerEndKm" : "activities" } });
            return Results.Conflict(new { code = failure.Code });
        }
    }
}
