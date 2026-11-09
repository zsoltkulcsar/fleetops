using FleetOps.Kernel.Authorization;
using FleetOps.People;

namespace FleetOps.Modules.Schedule;

public static class CycleGeneration
{
    public static void Validate(CyclePattern? pattern)
    {
        if (pattern is null) throw new ScheduleValidationException(["pattern"]);
        var fields = new List<string>();
        if (pattern.OnWeeks is < 1 or > 52) fields.Add("onWeeks");
        if (pattern.OffWeeks is < 0 or > 52) fields.Add("offWeeks");
        if (pattern.VehicleId == Guid.Empty) fields.Add("vehicleId");
        if (pattern.AnchorDate == default || pattern.AnchorDate > DateOnly.MaxValue.AddDays(-728)) fields.Add("anchorDate");
        if (fields.Count != 0) throw new ScheduleValidationException(fields);
    }

    public static IReadOnlyList<Cycle> Missing(Guid employeeId, CyclePattern pattern, DateOnly today, IReadOnlyList<Cycle> existing)
    {
        Validate(pattern);
        var periodDays = (pattern.OnWeeks + pattern.OffWeeks) * 7;
        var firstSlot = Math.Max(0, (int)Math.Floor((today.DayNumber - pattern.AnchorDate.DayNumber) / (double)periodDays));
        var horizon = today.AddDays(12 * 7);
        var occupied = existing.Where(cycle => cycle.PatternSlot.HasValue).Select(cycle => cycle.PatternSlot!.Value).ToHashSet();
        var generated = new List<Cycle>();
        for (var slot = firstSlot; ; slot++)
        {
            var startDay = (long)pattern.AnchorDate.DayNumber + (long)slot * periodDays;
            if (startDay >= horizon.DayNumber) break;
            var from = DateOnly.FromDayNumber((int)startDay);
            var to = from.AddDays(pattern.OnWeeks * 7 - 1);
            if (to < today || occupied.Contains(slot)) continue;
            generated.Add(new Cycle { EmployeeId = employeeId, VehicleId = pattern.VehicleId, PatternSlot = slot, From = from, To = to });
        }
        return generated;
    }

    public static bool Contains(Cycle cycle, DateOnly date) => cycle.From <= date &&
        (cycle.Status == CycleStatus.Overrun || cycle.Status == CycleStatus.Active && date <= cycle.To);
}

