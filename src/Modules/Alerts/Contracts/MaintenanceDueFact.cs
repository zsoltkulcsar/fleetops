namespace FleetOps.Modules.Alerts;

public sealed record MaintenanceDueFact(Guid Id, DateOnly? DueDate, int? DueKm, int? OdometerKm, DateTimeOffset? KmAsOf);
