namespace FleetOps.Api;

public sealed record SaveCyclePatternRequest(int Version, FleetOps.People.CyclePattern Pattern);
