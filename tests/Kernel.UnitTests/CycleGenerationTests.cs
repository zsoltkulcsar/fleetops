using FleetOps.Modules.Audit;
using FleetOps.Modules.Schedule;
using FleetOps.People;
using Xunit;

namespace FleetOps.Tests;

public sealed class CycleGenerationTests
{
    private static readonly Guid EmployeeId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid VehicleId = Guid.Parse("00000000-0000-0000-0000-000000000102");
    private static readonly DateOnly Today = new(2026, 11, 9);
    private static CyclePattern Pattern => new(2, 2, Today, VehicleId);

    [Fact]
    public void Twelve_weeks_are_generated_without_approval()
    {
        var cycles = CycleGeneration.Missing(EmployeeId, Pattern, Today, []);
        Assert.Equal(3, cycles.Count);
        Assert.Equal(new[] { Today, Today.AddDays(28), Today.AddDays(56) }, cycles.Select(cycle => cycle.From));
        Assert.Equal(new[] { Today.AddDays(13), Today.AddDays(41), Today.AddDays(69) }, cycles.Select(cycle => cycle.To));
        Assert.Equal(new int?[] { 0, 1, 2 }, cycles.Select(cycle => cycle.PatternSlot));
        Assert.All(cycles, cycle =>
        {
            Assert.Equal(EmployeeId, cycle.EmployeeId);
            Assert.Equal(VehicleId, cycle.VehicleId);
            Assert.Equal(CycleStatus.Planned, cycle.Status);
            Assert.Equal(CycleSource.Generated, cycle.Source);
            Assert.Null(cycle.FinishedAt);
            Assert.NotEqual(Guid.Empty, cycle.Id);
        });
    }

    [Fact]
    public void Running_again_does_not_generate_duplicate_slots()
    {
        var existing = CycleGeneration.Missing(EmployeeId, Pattern, Today, []);
        Assert.Empty(CycleGeneration.Missing(EmployeeId, Pattern, Today, existing));
    }

    [Fact]
    public void Moved_manual_cycle_retains_its_original_slot_and_is_not_recreated()
    {
        var manual = new Cycle { EmployeeId = EmployeeId, VehicleId = Guid.NewGuid(), PatternSlot = 1, From = Today.AddDays(31), To = Today.AddDays(44), Source = CycleSource.Manual, Version = 7 };
        var generated = CycleGeneration.Missing(EmployeeId, Pattern, Today, [manual]);
        Assert.Equal(new int?[] { 0, 2 }, generated.Select(cycle => cycle.PatternSlot));
        Assert.Equal(Today.AddDays(31), manual.From);
        Assert.Equal(7, manual.Version);
        Assert.Equal(CycleSource.Manual, manual.Source);
    }

    [Fact]
    public void Independent_manual_cycle_does_not_consume_a_pattern_slot()
    {
        var manual = new Cycle { EmployeeId = EmployeeId, PatternSlot = null, Source = CycleSource.Manual, From = Today, To = Today.AddDays(4) };
        Assert.Equal(3, CycleGeneration.Missing(EmployeeId, Pattern, Today, [manual]).Count);
    }

    [Fact]
    public void Off_week_does_not_create_a_cycle_for_the_off_period()
    {
        var today = Today.AddDays(20);
        var cycles = CycleGeneration.Missing(EmployeeId, Pattern, today, []);
        Assert.Equal(Today.AddDays(28), cycles[0].From);
        Assert.DoesNotContain(cycles, cycle => cycle.From <= today && cycle.To >= today);
    }

    [Fact]
    public void Rolling_horizon_keeps_the_current_tour_and_adds_new_slots()
    {
        var existing = CycleGeneration.Missing(EmployeeId, Pattern, Today, []);
        var missing = CycleGeneration.Missing(EmployeeId, Pattern, Today.AddDays(7), existing);
        var cycle = Assert.Single(missing);
        Assert.Equal(3, cycle.PatternSlot);
        Assert.Equal(Today.AddDays(84), cycle.From);
    }

    [Fact]
    public void Anchor_in_the_future_is_not_backfilled_before_the_anchor()
    {
        var anchor = Today.AddDays(14);
        var cycles = CycleGeneration.Missing(EmployeeId, Pattern with { AnchorDate = anchor }, Today, []);
        Assert.Equal(anchor, cycles[0].From);
        Assert.DoesNotContain(cycles, cycle => cycle.From < anchor);
    }

    [Fact]
    public void No_off_weeks_produces_contiguous_tours()
    {
        var cycles = CycleGeneration.Missing(EmployeeId, Pattern with { OnWeeks = 1, OffWeeks = 0 }, Today, []);
        Assert.Equal(12, cycles.Count);
        for (var index = 1; index < cycles.Count; index++)
            Assert.Equal(cycles[index - 1].To.AddDays(1), cycles[index].From);
    }

    [Fact]
    public void Calendar_dates_do_not_drift_across_daylight_saving_or_year_end()
    {
        var anchor = new DateOnly(2026, 10, 19);
        var cycles = CycleGeneration.Missing(EmployeeId, Pattern with { AnchorDate = anchor }, anchor, []);
        Assert.Equal(new DateOnly(2026, 11, 16), cycles[1].From);
        Assert.Equal(new DateOnly(2026, 12, 14), cycles[2].From);
        Assert.Equal(new DateOnly(2026, 12, 27), cycles[2].To);
    }

    [Theory]
    [InlineData(CycleStatus.Active, 0, true)]
    [InlineData(CycleStatus.Active, 13, true)]
    [InlineData(CycleStatus.Active, -1, false)]
    [InlineData(CycleStatus.Active, 14, false)]
    [InlineData(CycleStatus.Overrun, 14, true)]
    [InlineData(CycleStatus.Overrun, 40, true)]
    [InlineData(CycleStatus.Overrun, -1, false)]
    [InlineData(CycleStatus.Planned, 0, false)]
    [InlineData(CycleStatus.Done, 0, false)]
    [InlineData(CycleStatus.Cancelled, 0, false)]
    public void Membership_counts_only_active_and_overrun(CycleStatus status, int offset, bool expected)
    {
        var cycle = new Cycle { From = Today, To = Today.AddDays(13), Status = status };
        Assert.Equal(expected, CycleGeneration.Contains(cycle, Today.AddDays(offset)));
    }

    [Theory]
    [InlineData(0, 2, "onWeeks")]
    [InlineData(-1, 2, "onWeeks")]
    [InlineData(53, 2, "onWeeks")]
    [InlineData(2, -1, "offWeeks")]
    [InlineData(2, 53, "offWeeks")]
    public void Invalid_week_counts_report_only_field_names(int onWeeks, int offWeeks, string field)
    {
        var error = Assert.Throws<ScheduleValidationException>(() => CycleGeneration.Validate(Pattern with { OnWeeks = onWeeks, OffWeeks = offWeeks }));
        Assert.Equal(new[] { field }, error.Fields);
    }

    [Fact]
    public void Missing_pattern_and_required_fields_are_rejected()
    {
        Assert.Equal(new[] { "pattern" }, Assert.Throws<ScheduleValidationException>(() => CycleGeneration.Validate(null)).Fields);
        var invalid = Pattern with { VehicleId = Guid.Empty, AnchorDate = default };
        Assert.Equal(new[] { "vehicleId", "anchorDate" }, Assert.Throws<ScheduleValidationException>(() => CycleGeneration.Validate(invalid)).Fields);
    }

    [Fact]
    public void Every_cycle_field_has_an_audit_classification()
    {
        Assert.Empty(AuditArchitectureRules.ValidateAssembly(typeof(Cycle).Assembly));
    }
}
