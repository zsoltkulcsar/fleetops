namespace FleetOps.Api;
public sealed record RescheduleMaintenanceRequest(DateOnly ScheduledFor, string? Workshop, DateOnly? DueDate = null, int? DueKm = null);
