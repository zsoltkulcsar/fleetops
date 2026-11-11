namespace FleetOps.Modules.Maintenance;

public static class MaintenanceRules
{
    public static readonly IReadOnlySet<string> Types = new HashSet<string>(StringComparer.Ordinal)
    {
        "service", "repair", "inspection_hu", "inspection_sp", "tacho_check", "tyres", "other"
    };

    public static bool KmStale(DateTimeOffset? asOf, DateOnly today) =>
        asOf is null || today.DayNumber - DateOnly.FromDateTime(asOf.Value.Date).DayNumber > 30;

    public static bool IsOverdue(Maintenance maintenance, int? km, DateTimeOffset? asOf, DateOnly today) =>
        maintenance.Status is MaintenanceStatus.Scheduled or MaintenanceStatus.InProgress &&
        (maintenance.DueDate < today || !KmStale(asOf, today) && maintenance.DueKm.HasValue && km >= maintenance.DueKm);
}
