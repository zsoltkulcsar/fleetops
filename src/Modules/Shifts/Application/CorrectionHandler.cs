using System.Text.Json;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Shifts;
public sealed class CorrectionHandler(ICorrectionStore store, IShiftCommandStore shifts, ICompanyTimeService companyTime, TimeProvider clock) :
    IDriverCorrections, IQueryHandler<GetCorrectionsQuery, CorrectionList>, ICommandHandler<DecideCorrectionCommand, CorrectionList>
{
    public async Task<DriverCommandResult> ApplyAsync(DriverCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (!actor.Roles.Contains(RoleCode.Driver)) return new(command.CommandId, "failed", "permission_denied", null);
        try
        {
            var payload = command.Payload.Deserialize<CorrectionPayload>(ApplyDriverCommandHandler.JsonOptions) ?? throw new JsonException();
            if (payload.Reason is not ("wrong_times" or "wrong_activity" or "wrong_country" or "other" or "missing_shift") ||
                payload.Date == default || payload.ProposedStartAt == default(DateTimeOffset) || payload.ProposedEndAt == default(DateTimeOffset) ||
                payload.ProposedEndAt < payload.ProposedStartAt || payload.ProposedEndAt > clock.GetUtcNow() || payload.ProposedStartAt > clock.GetUtcNow() ||
                payload.Note?.Length > 1000 || payload.Note?.Any(char.IsControl) == true)
                throw new TimesheetValidationException("reason", "date", "proposedStartAt", "proposedEndAt", "note");
            Shift? shift = null;
            if (payload.Reason == "missing_shift")
            {
                if (payload.ShiftId is not null || payload.Date >= companyTime.ToCompanyDate(clock.GetUtcNow()) ||
                    !await store.MissingWorkdayAsync(actor.EmployeeId, payload.Date, context, cancellationToken))
                    throw new ShiftRuleException("not_missing_workday");
            }
            else
            {
                if (payload.ShiftId is not Guid shiftId || shiftId == Guid.Empty) throw new TimesheetValidationException("shiftId");
                shift = await shifts.LoadAsync(actor.EmployeeId, shiftId, context, cancellationToken) ?? throw new ShiftRuleException("shift_not_found");
                if (shift.Status is not (ShiftStatus.Submitted or ShiftStatus.Approved or ShiftStatus.Exported)) throw new ShiftRuleException("state_conflict");
                if (payload.Date != shift.Date) throw new TimesheetValidationException("date");
            }
            if (await store.HasOpenAsync(actor.EmployeeId, payload.ShiftId, payload.Date, context, cancellationToken)) throw new ShiftRuleException("correction_already_open");
            var request = new CorrectionRequest { Id = command.AggregateId, CommandId = command.CommandId, EmployeeId = actor.EmployeeId,
                ShiftId = payload.ShiftId, Date = payload.Date, Reason = payload.Reason, ProposedStartAt = payload.ProposedStartAt,
                ProposedEndAt = payload.ProposedEndAt, Note = payload.Note?.Trim(), RequiresHr = shift?.Status == ShiftStatus.Exported,
                Before = JsonSerializer.Serialize(Fields(shift)), RequestedAt = clock.GetUtcNow(), DeviceTime = command.DeviceTime };
            if (await store.LoadAsync(request.Id, context, cancellationToken) is not null) throw new ShiftRuleException("request_already_exists");
            await store.SaveAsync(request, actor, false, context, cancellationToken);
            return new(command.CommandId, "applied", null, null);
        }
        catch (TimesheetValidationException) { return new(command.CommandId, "failed", "invalid_payload", null); }
        catch (ShiftRuleException failure) { return new(command.CommandId, "failed", failure.Code, null); }
    }
    public async Task<CorrectionList> HandleAsync(GetCorrectionsQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var office = actor.Roles.Any(role => role is RoleCode.Dispatcher or RoleCode.Hr or RoleCode.Owner);
        return new(await store.ListAsync(office ? query.SubjectId : actor.EmployeeId, null, cancellationToken));
    }
    public async Task<CorrectionList> HandleAsync(DecideCorrectionCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (!actor.Roles.Any(role => role is RoleCode.Dispatcher or RoleCode.Hr or RoleCode.Owner))
            throw new AuthorizationException("permission_denied", "shifts.enter_edit", [RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner]);
        if (command.SubjectId is not Guid employeeId || employeeId == Guid.Empty) throw new TimesheetValidationException("employeeId");
        await shifts.LockEmployeeAsync(employeeId, context, cancellationToken);
        var request = await store.LoadAsync(command.RequestId, context, cancellationToken) ?? throw new ShiftRuleException("not_found");
        if (request.EmployeeId != employeeId) throw new ShiftRuleException("not_found");
        if (request.RequiresHr && !actor.Roles.Any(role => role is RoleCode.Hr or RoleCode.Owner))
            throw new AuthorizationException("permission_denied", "shifts.reopen_exported", [RoleCode.Hr, RoleCode.Owner]);
        if (request.Status != "open") throw new ShiftRuleException("state_conflict");
        if (command.Decision == "decline")
        {
            if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Length > 1000 || command.Reason.Any(char.IsControl)) throw new TimesheetValidationException("reason");
            request.Status = "declined";
            request.DeclineReason = command.Reason.Trim();
        }
        else if (command.Decision == "done")
        {
            var shiftId = request.ShiftId ?? command.ShiftId ?? throw new TimesheetValidationException("shiftId");
            if (request.ShiftId is not null && command.ShiftId is not null && request.ShiftId != command.ShiftId) throw new TimesheetValidationException("shiftId");
            var shift = await shifts.LoadAsync(employeeId, shiftId, context, cancellationToken) ?? throw new ShiftRuleException("shift_not_found");
            if (shift.Date != request.Date || shift.Status is ShiftStatus.Running or ShiftStatus.Exported) throw new ShiftRuleException("state_conflict");
            var before = JsonSerializer.Deserialize<Dictionary<string, string?>>(request.Before) ?? throw new InvalidOperationException("Invalid correction snapshot.");
            var after = Fields(shift);
            var diffs = before.Keys.Union(after.Keys).Where(field => before.GetValueOrDefault(field) != after.GetValueOrDefault(field))
                .Select(field => new CorrectionFieldDiff(field, before.GetValueOrDefault(field), after.GetValueOrDefault(field))).ToArray();
            if (diffs.Length == 0) throw new ShiftRuleException("no_changes");
            request.ShiftId = shift.Id;
            request.Diffs = JsonSerializer.Serialize(diffs);
            request.Status = "done";
        }
        else throw new TimesheetValidationException("decision");
        request.DecidedBy = actor.EmployeeId;
        request.DecidedAt = clock.GetUtcNow();
        await store.SaveAsync(request, actor, true, context, cancellationToken);
        return new(await store.ListAsync(employeeId, context, cancellationToken));
    }
    private static Dictionary<string, string?> Fields(Shift? shift)
    {
        if (shift is null) return [];
        return new()
        {
            ["startAt"] = shift.StartAt.ToString("O"), ["endAt"] = shift.EndAt?.ToString("O"),
            ["truckId"] = shift.TruckId?.ToString(), ["trailerId"] = shift.TrailerId?.ToString(),
            ["odometerStartKm"] = shift.OdometerStartKm?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["odometerEndKm"] = shift.OdometerEndKm?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["activities"] = JsonSerializer.Serialize(shift.Activities.OrderBy(activity => activity.StartAt).Select(activity => new { activity.Type, activity.StartAt, activity.EndAt })),
            ["overnights"] = JsonSerializer.Serialize(shift.Overnights.OrderBy(night => night.Night).Select(night => new { night.Night, night.CountryCode, night.Kind }))
        };
    }
}
