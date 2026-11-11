using System.Text.Json;
using System.Text.Json.Serialization;
using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;

namespace FleetOps.Modules.Shifts;

public sealed class ApplyDriverCommandHandler(IShiftCommandStore store, IShiftStartFacts facts, IShiftEvents events, ICompanyTimeService companyTime, ISettings settings, TimeProvider clock, IDriverDefectCommands defects)
    : ICommandHandler<ApplyDriverCommand, DriverCommandResult>
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, false) }
    };

    public async Task<DriverCommandResult> HandleAsync(ApplyDriverCommand request, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var command = request.Command;
        if (command is null || command.CommandId == Guid.Empty || command.AggregateId == Guid.Empty || command.BaseVersion < 0 ||
            command.DeviceTime == default || command.Payload.ValueKind != JsonValueKind.Object || command.Payload.GetRawText().Length > 65536 ||
            string.IsNullOrWhiteSpace(command.Type) || command.Type.Length > 60)
            throw new ShiftRuleException("invalid_command");
        await store.LockEmployeeAsync(actor.EmployeeId, context, cancellationToken);
        var prior = await store.ReceiptAsync(actor.EmployeeId, command.CommandId, context, cancellationToken);
        if (prior is not null)
            return prior.Type != command.Type ? new(command.CommandId, "failed", "command_id_reused", null)
                : JsonSerializer.Deserialize<DriverCommandResult>(prior.Result, JsonOptions) ?? throw new InvalidOperationException("invalid_command_receipt");
        var receivedAt = clock.GetUtcNow();
        if (command.Type is "defect.report" or "defect.cancel")
        {
            DriverCommandResult defectResult;
            try
            {
                defectResult = command.SchemaVersion != 1
                    ? new(command.CommandId, "failed", "unsupported_schema_version", null)
                    : await defects.ApplyAsync(command, actor, context, receivedAt, cancellationToken);
            }
            catch (JsonException) { defectResult = new(command.CommandId, "failed", "invalid_payload", null); }
            var defectReceipt = new CommandReceipt
            {
                EmployeeId = actor.EmployeeId, CommandId = command.CommandId, AggregateId = command.AggregateId,
                Type = command.Type, SchemaVersion = command.SchemaVersion, BaseVersion = command.BaseVersion,
                DeviceTime = command.DeviceTime, ServerReceivedAt = receivedAt, Payload = command.Payload.GetRawText(),
                Result = JsonSerializer.Serialize(defectResult, JsonOptions), Outcome = defectResult.Status
            };
            await store.SaveAsync(null, defectReceipt, null, context, cancellationToken);
            return defectResult;
        }
        var shift = await store.LoadAsync(actor.EmployeeId, command.AggregateId, context, cancellationToken);
        DriverCommandResult result;
        ConflictRecord? conflict = null;
        if (await store.HasFailedDependencyAsync(actor.EmployeeId, command.AggregateId, context, cancellationToken))
            result = new(command.CommandId, "failed", "dependency", null);
        else if (command.SchemaVersion != 1)
            result = new(command.CommandId, "failed", "unsupported_schema_version", null);
        else if (command.Type == "sync.test")
            result = new(command.CommandId, "applied", null, null);
        else if (shift is not null && (shift.Status is ShiftStatus.Submitted or ShiftStatus.Approved or ShiftStatus.Exported || shift.LastOfficeVersion > command.BaseVersion))
        {
            var office = ShiftViews.From(shift);
            conflict = new ConflictRecord { EmployeeId = actor.EmployeeId, ShiftId = shift.Id, CommandId = command.CommandId,
                BaseVersion = command.BaseVersion, OfficeVersion = shift.Version, DriverPayload = command.Payload.GetRawText(),
                OfficeSnapshot = JsonSerializer.Serialize(office, JsonOptions), CreatedAt = receivedAt };
            result = new(command.CommandId, "conflict", "office_version_stands", office);
            await events.ChangedAsync(actor.EmployeeId, shift.Id, "sync_conflict", context, cancellationToken);
        }
        else
        {
            try
            {
                if (command.Type == "shift.start")
                {
                    if (shift is not null) throw new ShiftRuleException("shift_already_exists");
                    if (command.BaseVersion != 0) throw new ShiftRuleException("dependency");
                    if (await store.HasRunningShiftAsync(actor.EmployeeId, context, cancellationToken)) throw new ShiftRuleException("running_shift_exists");
                    var payload = command.Payload.Deserialize<StartShiftPayload>(JsonOptions) ?? throw new ShiftRuleException("invalid_payload");
                    var startFacts = await facts.ReadAsync(actor.EmployeeId, payload.WorkWithoutVehicle ? null : payload.TruckId, payload.TrailerId, command.DeviceTime, context, cancellationToken);
                    if (!startFacts.Active) throw new ShiftRuleException(startFacts.EmployeeStatus == "suspended" ? "account_suspended" : "account_not_active");
                    var truckId = payload.WorkWithoutVehicle ? null : payload.TruckId ?? startFacts.SuggestedTruckId;
                    var blocked = truckId is not null && (!startFacts.DriverCanDrive || !startFacts.TruckUsable);
                    if (blocked && !payload.StartedOffline) throw new ShiftRuleException(startFacts.DriverCanDrive ? "truck_unusable" : "driver_blocked");
                    shift = ShiftRules.Start(command.AggregateId, actor.EmployeeId, companyTime.ToCompanyDate(command.DeviceTime), command.DeviceTime,
                        truckId, payload.TrailerId, payload.OdometerKm, payload.DepartureCheckOk);
                    shift.OverrideId = truckId is null ? null : startFacts.OverrideId;
                    shift.Activities[0].Id = command.AggregateId;
                    if (blocked) shift.BlockedAtStart = startFacts.DriverCanDrive ? "truck_unusable" : "driver_blocked";
                }
                else
                {
                    if (shift is null) throw new ShiftRuleException("shift_not_found");
                    if (command.BaseVersion > shift.Version) throw new ShiftRuleException("dependency");
                    ApplyChange(shift, command);
                }
                shift.DeviceTime = command.DeviceTime;
                shift.ServerReceivedAt = receivedAt;
                // The configured threshold is supplied by the composition layer.
                shift.DeviceTimeGap |= Math.Abs((receivedAt - command.DeviceTime).TotalMinutes) > await settings.GetIntAsync(new("company.device_time_gap_minutes"), context, cancellationToken);
                result = new(command.CommandId, "applied", null, ShiftViews.From(shift));
                if (command.Type == "shift.end" && command.Payload.TryGetProperty("finishedCycle", out var finished) && finished.ValueKind == JsonValueKind.True)
                    await events.ChangedAsync(actor.EmployeeId, shift.Id, "cycle_finished", context, cancellationToken);
                await events.ChangedAsync(actor.EmployeeId, shift.Id, shift.BlockedAtStart is null ? null : "blocked_shift", context, cancellationToken);
            }
            catch (JsonException) { result = new(command.CommandId, "failed", "invalid_payload", null); shift = null; }
            catch (ShiftRuleException failure) { result = new(command.CommandId, "failed", failure.Code, null); shift = null; }
        }
        var receipt = new CommandReceipt { EmployeeId = actor.EmployeeId, CommandId = command.CommandId, AggregateId = command.AggregateId,
            Type = command.Type, SchemaVersion = command.SchemaVersion, BaseVersion = command.BaseVersion, DeviceTime = command.DeviceTime,
            ServerReceivedAt = receivedAt, Payload = command.Payload.GetRawText(), Result = JsonSerializer.Serialize(result, JsonOptions), Outcome = result.Status };
        await store.SaveAsync(result.Status == "applied" ? shift : null, receipt, conflict, context, cancellationToken);
        return result;
    }

    private void ApplyChange(Shift shift, DriverCommand command)
    {
        switch (command.Type)
        {
            case "shift.switch":
                var activity = command.Payload.Deserialize<SwitchActivityPayload>(JsonOptions) ?? throw new ShiftRuleException("invalid_payload");
                var count = shift.Activities.Count;
                ShiftRules.Switch(shift, activity.Type, command.DeviceTime);
                if (shift.Activities.Count > count) shift.Activities[^1].Id = command.CommandId;
                break;
            case "shift.backdate":
                var backdate = command.Payload.Deserialize<BackdatePayload>(JsonOptions) ?? throw new ShiftRuleException("invalid_payload");
                ShiftRules.BackdateSwitch(shift, backdate.Minutes, command.DeviceTime);
                break;
            case "shift.end":
                var end = command.Payload.Deserialize<EndShiftPayload>(JsonOptions) ?? throw new ShiftRuleException("invalid_payload");
                if (end.EndAt > command.DeviceTime) throw new ShiftRuleException("end_after_device_time");
                ShiftRules.End(shift, end.EndAt ?? command.DeviceTime, end.OdometerKm);
                break;
            case "shift.night":
                var night = command.Payload.Deserialize<NightPayload>(JsonOptions) ?? throw new ShiftRuleException("invalid_payload");
                ShiftRules.RecordNight(shift, night.Night, night.CountryCode, night.Kind);
                break;
            case "shift.edit-activity":
                var edit = command.Payload.Deserialize<EditActivityPayload>(JsonOptions) ?? throw new ShiftRuleException("invalid_payload");
                if (edit.StartAt > command.DeviceTime || edit.EndAt > command.DeviceTime) throw new ShiftRuleException("activity_after_device_time");
                ShiftRules.EditActivity(shift, edit.ActivityId, edit.Type, edit.StartAt, edit.EndAt);
                break;
            case "shift.submit": ShiftRules.Submit(shift, clock.GetUtcNow()); break;
            case "sync.test": break;
            default: throw new ShiftRuleException("unsupported_command_type");
        }
    }
}

public sealed record StartShiftPayload(Guid? TruckId = null, Guid? TrailerId = null, int? OdometerKm = null, bool? DepartureCheckOk = null, bool WorkWithoutVehicle = false, bool StartedOffline = false);
public sealed record SwitchActivityPayload(ActivityType Type);
public sealed record BackdatePayload(int Minutes);
public sealed record EndShiftPayload(DateTimeOffset? EndAt = null, int? OdometerKm = null, bool? FinishedCycle = null);
public sealed record NightPayload(DateOnly Night, string CountryCode, OvernightKind Kind);
public sealed record EditActivityPayload(Guid ActivityId, ActivityType Type, DateTimeOffset StartAt, DateTimeOffset? EndAt);
