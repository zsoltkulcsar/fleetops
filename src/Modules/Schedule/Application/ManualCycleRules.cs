using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Schedule;

public static class ManualCycleRules
{
    public static void Validate(Guid employeeId, Guid vehicleId, DateOnly from, DateOnly to, string? reason)
    {
        var fields = new List<string>();
        if (employeeId == Guid.Empty) fields.Add("employeeId");
        if (vehicleId == Guid.Empty) fields.Add("vehicleId");
        if (from == default) fields.Add("from");
        if (to < from || to == default) fields.Add("to");
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) fields.Add("reason");
        if (fields.Count > 0) throw new ScheduleValidationException(fields);
    }
    // Equal endpoints are the shared handover day, not an overlap.
    public static bool Overlaps(DateOnly from, DateOnly to, DateOnly otherFrom, DateOnly otherTo) => from < otherTo && otherFrom < to;
}

