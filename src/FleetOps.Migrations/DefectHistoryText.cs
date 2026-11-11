using FleetOps.Modules.Fleet;

namespace FleetOps.Migrations;

/// <summary>
/// Turns audit entries of a DefectReport into words for the office history (O-08 detail): field labels instead of
/// property names, value labels instead of wire values, a photo count instead of file IDs, readable reasons instead of
/// reason codes. Internal bookkeeping fields are left out.
/// </summary>
public static class DefectHistoryText
{
    private static readonly Dictionary<string, string> FieldLabels = new(StringComparer.Ordinal)
    {
        ["Category"] = "Category", ["Severity"] = "Severity", ["Status"] = "Status", ["Description"] = "Description",
        ["Origin"] = "Origin", ["VehicleId"] = "Vehicle", ["Photos"] = "Photos", ["RepairId"] = "Repair",
        ["CloseReason"] = "Close reason", ["ReportedBy"] = "Reported by",
    };
    private static readonly HashSet<string> Hidden = new(StringComparer.Ordinal)
    {
        "CommandId", "DeviceTime", "ServerReceivedAt", "ReportedAt", "ClosedBy", "ClosedAt", "Version",
    };
    private static readonly Dictionary<string, string> ValueLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["brakes"] = "Brakes", ["lights"] = "Lights", ["tyres"] = "Tyres", ["engine"] = "Engine", ["body"] = "Body",
        ["tacho"] = "Tacho", ["load_securing"] = "Load securing", ["loadsecuring"] = "Load securing", ["other"] = "Other",
        ["minor"] = "Minor", ["major"] = "Major", ["critical"] = "Critical",
        ["open"] = "Open", ["acknowledged"] = "Acknowledged", ["in_repair"] = "In repair", ["inrepair"] = "In repair",
        ["fixed"] = "Fixed", ["closed"] = "Closed", ["cancelled"] = "Cancelled",
        ["app"] = "App", ["office_from_chat"] = "Logged from chat", ["officefromchat"] = "Logged from chat",
    };
    private static readonly Dictionary<string, string> Reasons = new(StringComparer.Ordinal)
    {
        ["driver_defect_report"] = "Reported in the app",
        ["office_from_chat"] = "Logged from a phone call or chat",
        ["office_defect_confirm"] = "Confirmed by dispatch",
        ["defect_cancelled"] = "Cancelled by the driver",
        ["repair_scheduled"] = "Repair booked",
        ["repair_changed"] = "Repair updated",
    };

    public static DefectHistoryItem? Readable(DefectHistoryItem item, int photoCount)
    {
        if (Hidden.Contains(item.FieldName)) return null;
        var field = FieldLabels.GetValueOrDefault(item.FieldName, item.FieldName);
        var reason = Reasons.GetValueOrDefault(item.Reason, item.Reason);
        if (item.FieldName == "Photos")
            return item with { FieldName = field, OldValue = null, NewValue = photoCount == 1 ? "1 added" : $"{photoCount} added", Reason = reason };
        if (item.FieldName is "VehicleId" or "RepairId" or "ReportedBy")
            return item with { FieldName = field, OldValue = null, NewValue = item.NewValue is null ? "removed" : "set", Reason = reason };
        return item with { FieldName = field, OldValue = Label(item.OldValue), NewValue = Label(item.NewValue), Reason = reason };
    }

    private static string? Label(string? value) => value is null ? null : ValueLabels.GetValueOrDefault(value, value);
}
