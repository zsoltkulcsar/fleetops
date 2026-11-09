namespace FleetOps.People;

public sealed record CyclePattern(int OnWeeks, int OffWeeks, DateOnly AnchorDate, Guid VehicleId);
