using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Absences;
using FleetOps.Modules.Schedule;
namespace FleetOps.Modules.Shifts;
public sealed class DriverPlanningCommands(IDriverAbsences absences, IScheduleRequests schedules) : IDriverPlanningCommands
{
    public async Task<DriverCommandResult> ApplyAsync(DriverCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (command.Type == "absence.request")
            {
                var payload = command.Payload.Deserialize<AbsenceRequestPayload>(ApplyDriverCommandHandler.JsonOptions) ?? throw new JsonException();
                await absences.RequestAsync(command.AggregateId, command.CommandId, command.DeviceTime, payload.From, payload.To, payload.HalfDay, payload.Note, actor, context, cancellationToken);
            }
            else if (command.Type == "absence.cancel")
            {
                await absences.CancelAsync(command.AggregateId, actor, context, cancellationToken);
            }
            else
            {
                var payload = command.Payload.Deserialize<ScheduleRequestPayload>(ApplyDriverCommandHandler.JsonOptions) ?? throw new JsonException();
                await schedules.RequestAsync(command.AggregateId, command.CommandId, command.DeviceTime, payload.CycleId, payload.ProposedFrom, payload.ProposedTo, payload.Note, actor, context, cancellationToken);
            }
            return new(command.CommandId, "applied", null, null);
        }
        catch (AbsenceValidationException) { return new(command.CommandId, "failed", "invalid_payload", null); }
        catch (ScheduleValidationException) { return new(command.CommandId, "failed", "invalid_payload", null); }
        catch (AbsenceConflictException failure) { return new(command.CommandId, "failed", failure.Code, null); }
        catch (ScheduleConflictException failure) { return new(command.CommandId, "failed", failure.Code, null); }
    }
}
