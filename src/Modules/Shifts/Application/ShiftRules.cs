using System.Globalization;

namespace FleetOps.Modules.Shifts;

public sealed class ShiftRuleException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public static class ShiftRules
{
    public static Shift Start(Guid id, Guid employeeId, DateOnly companyDate, DateTimeOffset at, Guid? truckId,
        Guid? trailerId = null, int? odometer = null, bool? departureCheck = null)
    {
        if (id == Guid.Empty || employeeId == Guid.Empty) throw new ShiftRuleException("invalid_shift");
        if (odometer < 0) throw new ShiftRuleException("invalid_odometer");
        if (truckId == Guid.Empty || trailerId == Guid.Empty || (truckId is null && trailerId is not null))
            throw new ShiftRuleException("invalid_vehicle");
        var shift = new Shift
        {
            Id = id, EmployeeId = employeeId, Date = companyDate, StartAt = at,
            TruckId = truckId, TrailerId = trailerId, OdometerStartKm = odometer,
            DepartureCheckOk = departureCheck == true ? true : null, DeviceTime = at
        };
        shift.Activities.Add(new ShiftActivity { ShiftId = id, Type = ActivityType.OtherWork, StartAt = at, RecordedAt = at });
        return shift;
    }

    public static void Switch(Shift shift, ActivityType type, DateTimeOffset at)
    {
        RequireRunning(shift);
        if (!Enum.IsDefined(type)) throw new ShiftRuleException("invalid_activity");
        if (type == ActivityType.Driving && shift.TruckId is null) throw new ShiftRuleException("vehicle_required_for_driving");
        var current = shift.Activities.Single(activity => activity.EndAt is null);
        if (at < current.StartAt) throw new ShiftRuleException("activity_time_before_current");
        if (current.Type == type) return;
        current.EndAt = at;
        shift.Activities.Add(new ShiftActivity { ShiftId = shift.Id, Type = type, StartAt = at, RecordedAt = at });
        shift.Version++;
    }

    public static void BackdateSwitch(Shift shift, int minutes, DateTimeOffset requestedAt)
    {
        RequireRunning(shift);
        if (minutes is not (5 or 15 or 30)) throw new ShiftRuleException("invalid_backdate_minutes");
        var activities = shift.Activities.OrderBy(activity => activity.StartAt).ThenBy(activity => activity.RecordedAt).ToArray();
        if (activities.Length < 2) throw new ShiftRuleException("no_switch_to_backdate");
        var current = activities[^1];
        var previous = activities[^2];
        if (requestedAt < current.RecordedAt || requestedAt - current.RecordedAt > TimeSpan.FromSeconds(5))
            throw new ShiftRuleException("backdate_window_closed");
        // A switch can be adjusted once; repeated commands cannot accumulate time.
        if (current.StartAt != current.RecordedAt) throw new ShiftRuleException("switch_already_backdated");
        var start = current.StartAt.AddMinutes(-minutes);
        if (start < previous.StartAt) throw new ShiftRuleException("backdate_before_previous_activity");
        previous.EndAt = start;
        current.StartAt = start;
        shift.Version++;
    }

    public static void EditActivity(Shift shift, Guid activityId, ActivityType type, DateTimeOffset start, DateTimeOffset? end)
    {
        RequireEditable(shift);
        if (!Enum.IsDefined(type)) throw new ShiftRuleException("invalid_activity");
        if (type == ActivityType.Driving && shift.TruckId is null) throw new ShiftRuleException("vehicle_required_for_driving");
        var activities = shift.Activities.OrderBy(activity => activity.StartAt).ThenBy(activity => activity.RecordedAt).ToArray();
        var index = Array.FindIndex(activities, activity => activity.Id == activityId);
        if (index < 0) throw new ShiftRuleException("activity_not_found");
        if (start < shift.StartAt || end < start || end > shift.EndAt ||
            (index == 0 && start != shift.StartAt) ||
            (index == activities.Length - 1 && end != shift.EndAt) ||
            (index > 0 && start < activities[index - 1].StartAt) ||
            (index < activities.Length - 1 && (end is null || end > activities[index + 1].EndAt)))
            throw new ShiftRuleException("invalid_activity_times");
        var activity = activities[index];
        activity.Type = type;
        activity.StartAt = start;
        activity.EndAt = end;
        if (index > 0) activities[index - 1].EndAt = start;
        if (index < activities.Length - 1) activities[index + 1].StartAt = end!.Value;
        shift.Version++;
    }

    public static void End(Shift shift, DateTimeOffset at, int? odometer = null)
    {
        RequireRunning(shift);
        var current = shift.Activities.Single(activity => activity.EndAt is null);
        if (at < current.StartAt) throw new ShiftRuleException("end_before_current_activity");
        if (odometer < 0 || (odometer is not null && shift.OdometerStartKm is not null && odometer < shift.OdometerStartKm))
            throw new ShiftRuleException("invalid_odometer");
        current.EndAt = at;
        shift.EndAt = at;
        shift.OdometerEndKm = odometer;
        shift.Status = ShiftStatus.Draft;
        shift.Version++;
    }

    public static void RecordNight(Shift shift, DateOnly night, string countryCode, OvernightKind kind)
    {
        RequireEditable(shift);
        if (night != shift.Date && night != shift.Date.AddDays(1)) throw new ShiftRuleException("invalid_overnight_date");
        if (!Enum.IsDefined(kind)) throw new ShiftRuleException("invalid_overnight_kind");
        if (countryCode is null || countryCode.Length != 2 || countryCode.Any(character => character is < 'A' or > 'Z'))
            throw new ShiftRuleException("invalid_country");
        try
        {
            if (new RegionInfo(countryCode).TwoLetterISORegionName != countryCode) throw new ShiftRuleException("invalid_country");
        }
        catch (ArgumentException) { throw new ShiftRuleException("invalid_country"); }
        var overnight = shift.Overnights.SingleOrDefault(item => item.Night == night);
        if (overnight is null)
        {
            overnight = new Overnight { ShiftId = shift.Id, Night = night };
            shift.Overnights.Add(overnight);
        }
        overnight.CountryCode = countryCode;
        overnight.Kind = kind;
        shift.Version++;
    }

    public static void Submit(Shift shift)
    {
        if (shift.Status is not (ShiftStatus.Draft or ShiftStatus.Rejected)) throw new ShiftRuleException("shift_not_ready_to_submit");
        if (shift.EndAt is null || shift.Activities.Any(activity => activity.EndAt is null)) throw new ShiftRuleException("shift_not_ended");
        shift.Status = ShiftStatus.Submitted;
        shift.RejectReason = null;
        shift.Version++;
    }

    public static decimal Hours(Shift shift, ActivityType? type = null, DateTimeOffset? runningAsOf = null)
    {
        var seconds = shift.Activities.Where(activity => type is null || activity.Type == type)
            .Sum(activity => ((activity.EndAt ?? runningAsOf ?? activity.StartAt) - activity.StartAt).TotalSeconds);
        return (decimal)seconds / 3600m;
    }

    private static void RequireRunning(Shift shift)
    {
        if (shift.Status != ShiftStatus.Running) throw new ShiftRuleException("shift_not_running");
    }

    private static void RequireEditable(Shift shift)
    {
        if (shift.Status is not (ShiftStatus.Running or ShiftStatus.Draft or ShiftStatus.Rejected))
            throw new ShiftRuleException("shift_locked");
    }
}
