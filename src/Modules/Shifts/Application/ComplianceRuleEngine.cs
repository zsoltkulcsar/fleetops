using System.Text.Json;
using FleetOps.Modules.Setup;
namespace FleetOps.Modules.Shifts;
public static class ComplianceRuleEngine
{
    public static IReadOnlyList<RuleWarning> ShiftWarnings(Shift shift, IReadOnlyList<Shift> history, IReadOnlyList<ComplianceRuleView> rules, DateTimeOffset now)
    {
        var warnings = new List<RuleWarning>();
        var driving = Minutes(shift, ActivityType.Driving, now);
        var dailyDriving = driving + history.Where(day => day.Id != shift.Id && day.Date == shift.Date).Sum(day => Minutes(day, ActivityType.Driving, now));
        var work = driving + Minutes(shift, ActivityType.OtherWork, now);
        var breaks = Minutes(shift, ActivityType.Break, now);
        var monday = shift.Date.AddDays(-(((int)shift.Date.DayOfWeek + 6) % 7));
        Evaluate("C-01", value =>
        {
            var extensions = history.Where(day => day.Date >= monday && day.Date < shift.Date).GroupBy(day => day.Date)
                .Count(days => days.Sum(day => Minutes(day, ActivityType.Driving, now)) > Number(value, "minutes"));
            return dailyDriving > Number(value, "extendedMinutes") || dailyDriving > Number(value, "minutes") && extensions >= Number(value, "extensionsPerWeek");
        });
        Evaluate("C-04", value =>
        {
            double continuous = 0;
            var firstSplit = false;
            foreach (var activity in shift.Activities.OrderBy(activity => activity.StartAt))
            {
                var duration = Math.Max(0, ((activity.EndAt ?? now) - activity.StartAt).TotalMinutes);
                if (activity.Type == ActivityType.Driving)
                {
                    continuous += duration;
                    if (continuous > Number(value, "drivingMinutes")) return true;
                }
                if (activity.Type != ActivityType.Break) continue;
                if (duration >= Number(value, "breakMinutes") || firstSplit && duration >= Number(value, "splitLastMinutes"))
                {
                    continuous = 0;
                    firstSplit = false;
                }
                else if (duration >= Number(value, "splitFirstMinutes")) firstSplit = true;
            }
            return false;
        });
        Evaluate("C-05", value =>
        {
            var previous = history.Where(day => day.Id != shift.Id && day.EndAt <= shift.StartAt).OrderByDescending(day => day.EndAt).FirstOrDefault();
            if (previous?.EndAt is not DateTimeOffset end) return false;
            var rest = (shift.StartAt - end).TotalMinutes;
            if (rest >= Number(value, "minutes")) return false;
            if (rest < Number(value, "reducedMinutes")) return true;
            var ordered = history.Where(day => day.Id != shift.Id && day.EndAt is not null && day.StartAt < shift.StartAt).OrderBy(day => day.StartAt).ToArray();
            var reduced = 0;
            var weeklyRule = rules.SingleOrDefault(rule => rule.Id == "C-06");
            using var weeklyValue = weeklyRule?.Value is null ? null : JsonDocument.Parse(weeklyRule.Value);
            for (var index = 1; index < ordered.Length; index++)
            {
                var gap = (ordered[index].StartAt - ordered[index - 1].EndAt!.Value).TotalMinutes;
                if (weeklyValue is not null && gap >= Number(weeklyValue.RootElement, "reducedMinutes")) reduced = 0;
                else if (gap < Number(value, "minutes")) reduced++;
            }
            return reduced >= Number(value, "reductions");
        });
        Evaluate("C-07", value => work > Number(value, "longWorkMinutes") ? breaks < Number(value, "longBreakMinutes") :
            work > Number(value, "workMinutes") && breaks < Number(value, "breakMinutes"));
        return warnings;
        void Evaluate(string id, Func<JsonElement, bool> exceeds)
        {
            var rule = rules.SingleOrDefault(rule => rule.Id == id);
            if (rule?.Value is null) return;
            using var value = JsonDocument.Parse(rule.Value);
            if (exceeds(value.RootElement)) warnings.Add(Warning(rule));
        }
    }
    public static IReadOnlyList<RuleWarning> WeekWarnings(DateOnly monday, IReadOnlyList<Shift> shifts, IReadOnlyList<ComplianceRuleView> rules, DateTimeOffset now)
    {
        var current = shifts.Where(shift => shift.Date >= monday && shift.Date < monday.AddDays(7)).ToArray();
        var fortnight = shifts.Where(shift => shift.Date >= monday.AddDays(-7) && shift.Date < monday.AddDays(7)).ToArray();
        var warnings = new List<RuleWarning>();
        Check("C-02", current.Sum(shift => Minutes(shift, ActivityType.Driving, now)));
        Check("C-03", fortnight.Sum(shift => Minutes(shift, ActivityType.Driving, now)));
        Check("C-08", current.Sum(shift => Minutes(shift, ActivityType.Driving, now) + Minutes(shift, ActivityType.OtherWork, now)));
        return warnings;
        void Check(string id, double minutes)
        {
            var rule = rules.SingleOrDefault(rule => rule.Id == id);
            if (rule?.Value is null) return;
            using var value = JsonDocument.Parse(rule.Value);
            if (minutes > Number(value.RootElement, "minutes")) warnings.Add(Warning(rule));
        }
    }
    private static double Number(JsonElement value, string name) => value.GetProperty(name).GetDouble();
    private static double Minutes(Shift shift, ActivityType type, DateTimeOffset now) => shift.Activities.Where(activity => activity.Type == type)
        .Sum(activity => Math.Max(0, ((activity.EndAt ?? shift.EndAt ?? now) - activity.StartAt).TotalMinutes));
    private static RuleWarning Warning(ComplianceRuleView rule) => new(rule.Id, rule.Confirmed ? "warning" : "info",
        rule.Confirmed ? "Possible breach \u2014 check against tachograph" : "Guide value \u00b7 rule not yet confirmed");
}
