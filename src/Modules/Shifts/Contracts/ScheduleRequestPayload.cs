namespace FleetOps.Modules.Shifts;
public sealed record ScheduleRequestPayload(Guid CycleId, DateOnly ProposedFrom, DateOnly ProposedTo, string? Note);
