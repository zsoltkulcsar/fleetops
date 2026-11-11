namespace FleetOps.Api;
public sealed record LeavePersonRequest(DateOnly? EndDate, string? Reason, DateTimeOffset? RunningShiftEnd = null, string? RunningShiftReason = null);
