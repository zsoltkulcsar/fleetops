namespace FleetOps.Api;
public sealed record DoneRepairRequest(DateOnly? DoneAt = null, int? OdometerAtDoneKm = null);
