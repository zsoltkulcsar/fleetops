namespace FleetOps.Modules.Fleet;

public static class DefectRules
{
    public static bool IsOpen(DefectReport defect) => defect.Status is DefectStatus.Open or DefectStatus.Acknowledged or DefectStatus.InRepair;

    public static bool BlocksVehicle(DefectReport defect) => IsOpen(defect) && defect.Severity == DefectSeverity.Critical;

    public static void Validate(DefectReport defect)
    {
        var fields = new List<string>();
        if (defect.Id == Guid.Empty) fields.Add("id");
        if (defect.VehicleId == Guid.Empty) fields.Add("vehicleId");
        if (defect.ReportedBy == Guid.Empty) fields.Add("reportedBy");
        if (defect.ReportedAt == default) fields.Add("reportedAt");
        if (defect.DeviceTime == default) fields.Add("deviceTime");
        if (defect.ServerReceivedAt == default) fields.Add("serverReceivedAt");
        if (!Enum.IsDefined(defect.Category)) fields.Add("category");
        if (!Enum.IsDefined(defect.Severity)) fields.Add("severity");
        if (!Enum.IsDefined(defect.Origin)) fields.Add("origin");
        if (defect.Description?.Length > 4000) fields.Add("description");
        if (defect.Photos is null || defect.Photos.Length > 5 || defect.Photos.Any(photo => photo == Guid.Empty) || defect.Photos.Distinct().Count() != defect.Photos.Length)
            fields.Add("photos");
        if (string.IsNullOrWhiteSpace(defect.Description) && (defect.Photos is null || defect.Photos.Length == 0)) fields.Add("description");
        if (defect.Origin == DefectOrigin.App && (defect.CommandId is null || defect.CommandId == Guid.Empty)) fields.Add("commandId");
        if (fields.Count > 0) throw new VehicleValidationException(fields);
    }

    public static void Confirm(DefectReport defect)
    {
        if (defect.Status != DefectStatus.Open) throw new VehicleConflictException("not_confirmable");
        defect.Status = DefectStatus.Acknowledged;
    }

    public static void Downgrade(DefectReport defect, DefectSeverity severity, string? reason)
    {
        RequireReason(reason);
        if (!Enum.IsDefined(severity)) throw new VehicleValidationException(["severity"]);
        if (!IsOpen(defect) || severity >= defect.Severity) throw new VehicleConflictException("not_downgradable");
        defect.Severity = severity;
    }

    public static void Cancel(DefectReport defect, Guid actorId, DateTimeOffset deviceTime, DateTimeOffset receivedAt)
    {
        if (defect.ReportedBy != actorId || defect.Origin != DefectOrigin.App || defect.Severity != DefectSeverity.Critical || defect.Status != DefectStatus.Open)
            throw new VehicleConflictException("not_cancellable");
        var elapsed = deviceTime - defect.DeviceTime;
        if (elapsed < TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(60)) throw new VehicleConflictException("cancel_window_passed");
        defect.Status = DefectStatus.Cancelled;
        defect.ClosedBy = actorId;
        defect.ClosedAt = receivedAt;
        defect.CloseReason = "Cancelled by driver within 60 s";
    }

    public static void Close(DefectReport defect, Guid actorId, DateTimeOffset now, string? reason, bool hasRepair)
    {
        RequireReason(reason);
        if (!IsOpen(defect) || hasRepair) throw new VehicleConflictException("not_closable");
        defect.Status = DefectStatus.Closed;
        defect.ClosedBy = actorId;
        defect.ClosedAt = now;
        defect.CloseReason = reason!.Trim();
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 4000) throw new VehicleValidationException(["reason"]);
    }
}
