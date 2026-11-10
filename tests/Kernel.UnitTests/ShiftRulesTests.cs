using FleetOps.Modules.Shifts;
using Xunit;

namespace FleetOps.Tests;

public sealed class ShiftRulesTests
{
    [Fact]
    public void Shift_commands_and_entities_declare_authorization_and_audit_contracts()
    {
        Assert.Empty(FleetOps.Kernel.Authorization.CommandArchitectureRules.ValidateAssembly(typeof(Shift).Assembly));
        Assert.Empty(FleetOps.Modules.Audit.AuditArchitectureRules.ValidateAssembly(typeof(Shift).Assembly));
    }
    private static readonly DateTimeOffset StartAt = DateTimeOffset.Parse("2026-11-10T08:00:00Z");
    private static Shift Started(Guid? truck = null) => ShiftRules.Start(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 11, 10), StartAt, truck);

    [Fact]
    public void Starting_creates_other_work_and_keeps_optional_values_empty()
    {
        var shift = Started();
        var activity = Assert.Single(shift.Activities);
        Assert.Equal(ActivityType.OtherWork, activity.Type);
        Assert.Equal(StartAt, activity.StartAt);
        Assert.Null(activity.EndAt);
        Assert.Equal(shift.Id, activity.ShiftId);
        Assert.Equal(ShiftStatus.Running, shift.Status);
        Assert.Null(shift.TruckId);
        Assert.Null(shift.TrailerId);
        Assert.Null(shift.OdometerStartKm);
        Assert.Null(shift.DepartureCheckOk);
        Assert.Equal(1, shift.Version);
    }

    [Fact]
    public void Switching_closes_the_previous_activity_without_gaps_and_moves_parent_version()
    {
        var shift = Started(Guid.NewGuid());
        ShiftRules.Switch(shift, ActivityType.Driving, StartAt.AddHours(1));
        Assert.Equal(2, shift.Activities.Count);
        Assert.Equal(shift.Activities[0].EndAt, shift.Activities[1].StartAt);
        Assert.Equal(ActivityType.Driving, shift.Activities[1].Type);
        Assert.Null(shift.Activities[1].EndAt);
        Assert.Equal(2, shift.Version);
    }

    [Fact]
    public void Non_driving_shift_cannot_switch_or_edit_to_driving()
    {
        var shift = Started();
        var switchError = Assert.Throws<ShiftRuleException>(() => ShiftRules.Switch(shift, ActivityType.Driving, StartAt.AddMinutes(10)));
        Assert.Equal("vehicle_required_for_driving", switchError.Code);
        var editError = Assert.Throws<ShiftRuleException>(() => ShiftRules.EditActivity(shift, shift.Activities[0].Id, ActivityType.Driving, StartAt, null));
        Assert.Equal("vehicle_required_for_driving", editError.Code);
        Assert.Equal(ActivityType.OtherWork, Assert.Single(shift.Activities).Type);
        Assert.Equal(1, shift.Version);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    public void Backdating_within_five_seconds_keeps_activities_contiguous(int minutes)
    {
        var shift = Started(Guid.NewGuid());
        var switchedAt = StartAt.AddHours(1);
        ShiftRules.Switch(shift, ActivityType.Driving, switchedAt);
        ShiftRules.BackdateSwitch(shift, minutes, switchedAt.AddSeconds(5));
        Assert.Equal(switchedAt.AddMinutes(-minutes), shift.Activities[1].StartAt);
        Assert.Equal(shift.Activities[1].StartAt, shift.Activities[0].EndAt);
        Assert.Equal(3, shift.Version);
    }

    [Fact]
    public void Backdating_after_the_window_is_refused_without_changing_times()
    {
        var shift = Started(Guid.NewGuid());
        var switchedAt = StartAt.AddHours(1);
        ShiftRules.Switch(shift, ActivityType.Driving, switchedAt);
        var error = Assert.Throws<ShiftRuleException>(() => ShiftRules.BackdateSwitch(shift, 15, switchedAt.AddSeconds(6)));
        Assert.Equal("backdate_window_closed", error.Code);
        Assert.Equal(switchedAt, shift.Activities[1].StartAt);
        Assert.Equal(2, shift.Version);
    }

    [Fact]
    public void Repeated_backdating_does_not_accumulate_adjustments()
    {
        var shift = Started(Guid.NewGuid());
        var switchedAt = StartAt.AddHours(1);
        ShiftRules.Switch(shift, ActivityType.Driving, switchedAt);
        ShiftRules.BackdateSwitch(shift, 5, switchedAt.AddSeconds(1));
        var error = Assert.Throws<ShiftRuleException>(() => ShiftRules.BackdateSwitch(shift, 30, switchedAt.AddSeconds(2)));
        Assert.Equal("switch_already_backdated", error.Code);
        Assert.Equal(switchedAt.AddMinutes(-5), shift.Activities[1].StartAt);
        Assert.Equal(3, shift.Version);
    }

    [Fact]
    public void Backdating_cannot_overlap_an_earlier_activity()
    {
        var shift = Started(Guid.NewGuid());
        var switchedAt = StartAt.AddMinutes(10);
        ShiftRules.Switch(shift, ActivityType.Driving, switchedAt);
        var error = Assert.Throws<ShiftRuleException>(() => ShiftRules.BackdateSwitch(shift, 30, switchedAt.AddSeconds(1)));
        Assert.Equal("backdate_before_previous_activity", error.Code);
        Assert.Equal(StartAt, shift.Activities[0].StartAt);
        Assert.Equal(switchedAt, shift.Activities[0].EndAt);
    }

    [Fact]
    public void Ending_creates_a_draft_without_requiring_an_odometer()
    {
        var shift = Started();
        ShiftRules.End(shift, StartAt.AddHours(8));
        Assert.Equal(ShiftStatus.Draft, shift.Status);
        Assert.Equal(StartAt.AddHours(8), shift.EndAt);
        Assert.Equal(shift.EndAt, Assert.Single(shift.Activities).EndAt);
        Assert.Null(shift.OdometerEndKm);
        Assert.Equal(8m, ShiftRules.Hours(shift));
    }

    [Fact]
    public void Invalid_odometer_refuses_end_without_changing_running_shift()
    {
        var shift = ShiftRules.Start(Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 11, 10), StartAt, Guid.NewGuid(), odometer: 500);
        var error = Assert.Throws<ShiftRuleException>(() => ShiftRules.End(shift, StartAt.AddHours(8), 499));
        Assert.Equal("invalid_odometer", error.Code);
        Assert.Equal(ShiftStatus.Running, shift.Status);
        Assert.Null(shift.EndAt);
        Assert.Null(Assert.Single(shift.Activities).EndAt);
    }

    [Fact]
    public void Edited_boundary_updates_neighbor_and_parent_version()
    {
        var shift = Started(Guid.NewGuid());
        ShiftRules.Switch(shift, ActivityType.Driving, StartAt.AddHours(1));
        var driving = shift.Activities[1];
        ShiftRules.EditActivity(shift, driving.Id, ActivityType.Driving, StartAt.AddMinutes(45), null);
        Assert.Equal(StartAt.AddMinutes(45), shift.Activities[0].EndAt);
        Assert.Equal(shift.Activities[0].EndAt, driving.StartAt);
        Assert.Equal(3, shift.Version);
    }

    [Fact]
    public void Overnight_update_keeps_one_row_per_night_and_moves_parent_version()
    {
        var shift = Started();
        ShiftRules.RecordNight(shift, shift.Date, "DE", OvernightKind.Cab);
        var originalId = Assert.Single(shift.Overnights).Id;
        ShiftRules.RecordNight(shift, shift.Date, "FR", OvernightKind.Hotel);
        var overnight = Assert.Single(shift.Overnights);
        Assert.Equal(originalId, overnight.Id);
        Assert.Equal("FR", overnight.CountryCode);
        Assert.Equal(OvernightKind.Hotel, overnight.Kind);
        Assert.Equal(3, shift.Version);
    }

    [Theory]
    [InlineData("ZZ")]
    [InlineData("Germany")]
    [InlineData("de")]
    public void Invalid_country_does_not_create_an_overnight(string country)
    {
        var shift = Started();
        var error = Assert.Throws<ShiftRuleException>(() => ShiftRules.RecordNight(shift, shift.Date, country, OvernightKind.Cab));
        Assert.Equal("invalid_country", error.Code);
        Assert.Empty(shift.Overnights);
        Assert.Equal(1, shift.Version);
    }

    [Theory]
    [InlineData(ShiftStatus.Submitted)]
    [InlineData(ShiftStatus.Approved)]
    [InlineData(ShiftStatus.Exported)]
    public void Locked_shifts_refuse_activity_and_overnight_edits(ShiftStatus status)
    {
        var shift = Started();
        ShiftRules.End(shift, StartAt.AddHours(8));
        shift.Status = status;
        var version = shift.Version;
        Assert.Equal("shift_locked", Assert.Throws<ShiftRuleException>(() => ShiftRules.RecordNight(shift, shift.Date, "DE", OvernightKind.Cab)).Code);
        Assert.Equal("shift_locked", Assert.Throws<ShiftRuleException>(() => ShiftRules.EditActivity(shift, shift.Activities[0].Id, ActivityType.Break, shift.StartAt, shift.EndAt)).Code);
        Assert.Equal(version, shift.Version);
        Assert.Empty(shift.Overnights);
    }

    [Fact]
    public void Rejected_shift_can_be_fixed_and_resubmitted_with_reason_cleared()
    {
        var shift = Started();
        ShiftRules.End(shift, StartAt.AddHours(8));
        shift.Status = ShiftStatus.Rejected;
        shift.RejectReason = "Check the night";
        ShiftRules.RecordNight(shift, shift.Date, "DE", OvernightKind.Cab);
        ShiftRules.Submit(shift);
        Assert.Equal(ShiftStatus.Submitted, shift.Status);
        Assert.Null(shift.RejectReason);
        Assert.Equal(4, shift.Version);
        Assert.Single(shift.Overnights);
    }

    [Theory]
    [InlineData("2026-03-28T12:00:00+01:00", "2026-03-29T12:00:00+02:00", 23)]
    [InlineData("2026-10-24T12:00:00+02:00", "2026-10-25T12:00:00+01:00", 25)]
    public void Cross_midnight_DST_totals_use_real_elapsed_hours_and_keep_start_date(string start, string end, int hours)
    {
        var startTime = DateTimeOffset.Parse(start);
        var companyDate = DateOnly.FromDateTime(startTime.DateTime);
        var shift = ShiftRules.Start(Guid.NewGuid(), Guid.NewGuid(), companyDate, startTime, null);
        ShiftRules.End(shift, DateTimeOffset.Parse(end));
        Assert.Equal(hours, ShiftRules.Hours(shift));
        Assert.Equal(companyDate, shift.Date);
    }
}
