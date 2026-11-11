using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;

namespace FleetOps.Modules.Shifts;

public sealed class DriverDefectCommands(IDefectStore store, IDefectEvents events) : IDriverDefectCommands
{
    public async Task<DriverCommandResult> ApplyAsync(DriverCommand command, AccessContext actor, CommandContext context, DateTimeOffset receivedAt, CancellationToken cancellationToken)
    {
        try
        {
            if (command.Type == "defect.report")
            {
                var payload = command.Payload.Deserialize<ReportDefectPayload>(ApplyDriverCommandHandler.JsonOptions) ?? throw new VehicleValidationException(["payload"]);
                var defect = new DefectReport
                {
                    Id = command.AggregateId, CommandId = command.CommandId, VehicleId = payload.VehicleId,
                    ReportedBy = actor.EmployeeId, ReportedAt = command.DeviceTime, DeviceTime = command.DeviceTime,
                    ServerReceivedAt = receivedAt, Origin = DefectOrigin.App, Category = payload.Category,
                    Severity = payload.Severity, Description = payload.Description, Photos = payload.Photos ?? []
                };
                DefectRules.Validate(defect);
                await store.LockVehicleAsync(defect.VehicleId, context, cancellationToken);
                await store.ValidateReportAsync(defect, context, cancellationToken);
                var wasOffRoad = await store.IsOffRoadAsync(defect.VehicleId, context, cancellationToken);
                await store.SaveAsync(defect, actor.EmployeeId, context, cancellationToken);
                await events.ReportedAsync(defect, DefectRules.BlocksVehicle(defect) && !wasOffRoad, context, cancellationToken);
            }
            else
            {
                var payload = command.Payload.Deserialize<CancelDefectPayload>(ApplyDriverCommandHandler.JsonOptions) ?? throw new VehicleValidationException(["payload"]);
                var defect = await store.LoadAsync(payload.DefectId ?? command.AggregateId, context, cancellationToken) ?? throw new VehicleConflictException("not_cancellable");
                await store.LockVehicleAsync(defect.VehicleId, context, cancellationToken);
                defect = await store.LoadAsync(payload.DefectId ?? command.AggregateId, context, cancellationToken) ?? throw new VehicleConflictException("not_cancellable");
                DefectRules.Cancel(defect, actor.EmployeeId, command.DeviceTime, receivedAt);
                await store.SaveAsync(defect, actor.EmployeeId, context, cancellationToken);
                await events.CancelledAsync(defect, context, cancellationToken);
            }
            return new(command.CommandId, "applied", null, null);
        }
        catch (VehicleValidationException) { return new(command.CommandId, "failed", "invalid_payload", null); }
        catch (VehicleConflictException failure) { return new(command.CommandId, "failed", failure.Code, null); }
    }
}
