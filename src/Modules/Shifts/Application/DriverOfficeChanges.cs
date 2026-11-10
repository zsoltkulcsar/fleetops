namespace FleetOps.Modules.Shifts;

public static class DriverOfficeChanges
{
    public static IReadOnlyList<ShiftHistory> Project(IReadOnlyList<ShiftHistory> history, IReadOnlyDictionary<Guid, string> plates) =>
        history.Where(entry => IsVisible(entry.EntityType, entry.Field))
            .Select(entry => entry with { Before = Value(entry.Field, entry.Before, plates), After = Value(entry.Field, entry.After, plates) }).ToArray();

    private static bool IsVisible(string entity, string field) => entity switch
    {
        nameof(Shift) => field is nameof(Shift.StartAt) or nameof(Shift.EndAt) or nameof(Shift.TruckId) or nameof(Shift.TrailerId) or
            nameof(Shift.OdometerStartKm) or nameof(Shift.OdometerEndKm) or nameof(Shift.Status),
        nameof(ShiftActivity) => field is nameof(ShiftActivity.StartAt) or nameof(ShiftActivity.EndAt) or nameof(ShiftActivity.Type),
        nameof(Overnight) => field is nameof(Overnight.Night) or nameof(Overnight.CountryCode) or nameof(Overnight.Kind),
        _ => false
    };

    private static string? Value(string field, string? value, IReadOnlyDictionary<Guid, string> plates)
    {
        if (value is null) return null;
        if (field is nameof(Shift.TruckId) or nameof(Shift.TrailerId))
            return Guid.TryParse(value, out var vehicleId) ? plates.GetValueOrDefault(vehicleId, "Unknown vehicle") : "Unknown vehicle";
        if (field is nameof(Shift.Status) or nameof(ShiftActivity.Type) or nameof(Overnight.Kind))
            return value == "OtherWork" ? "other_work" : value.ToLowerInvariant();
        return value;
    }
}
