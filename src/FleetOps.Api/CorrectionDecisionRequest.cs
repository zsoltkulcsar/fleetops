namespace FleetOps.Api;
public sealed record CorrectionDecisionRequest(Guid EmployeeId, string Decision, string? Reason = null, Guid? ShiftId = null);
