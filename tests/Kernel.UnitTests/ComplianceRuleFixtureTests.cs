using System.Text.Json;
using FleetOps.Modules.Setup;
using FleetOps.Modules.Shifts;
using Xunit;
namespace FleetOps.Tests;
public sealed class ComplianceRuleFixtureTests
{
    [Fact]
    public void Rule_cases_fixture_matches_the_server_engine()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tests", "fixtures", "rule-cases.json")));
        foreach (var testCase in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var rules = testCase.GetProperty("rules").EnumerateArray().Select(rule => new ComplianceRuleView(
                rule.GetProperty("id").GetString()!, "Fixture", "Fixture guide", rule.GetProperty("value").ValueKind == JsonValueKind.Null ? null : rule.GetProperty("value").GetRawText(), rule.GetProperty("confirmed").GetBoolean())).ToArray();
            var start = new DateTimeOffset(2026, 11, 9, 6, 0, 0, TimeSpan.Zero);
            var shift = new Shift { Id = Guid.NewGuid(), Date = new(2026, 11, 9), StartAt = start };
            var cursor = start;
            foreach (var input in testCase.GetProperty("activities").EnumerateArray())
            {
                var end = cursor.AddMinutes(input.GetProperty("minutes").GetInt32());
                var type = input.GetProperty("type").GetString() switch
                {
                    "driving" => ActivityType.Driving, "other_work" => ActivityType.OtherWork,
                    "break" => ActivityType.Break, "availability" => ActivityType.Availability,
                    _ => throw new InvalidOperationException("Unknown fixture activity.")
                };
                shift.Activities.Add(new ShiftActivity { Id = Guid.NewGuid(), ShiftId = shift.Id, Type = type, StartAt = cursor, EndAt = end });
                cursor = end;
            }
            shift.EndAt = cursor;
            var warnings = testCase.TryGetProperty("scope", out var scope) && scope.GetString() == "week"
                ? ComplianceRuleEngine.WeekWarnings(shift.Date, [shift], rules, cursor)
                : ComplianceRuleEngine.ShiftWarnings(shift, [], rules, cursor);
            var expected = testCase.GetProperty("expectedWarnings").EnumerateArray().Select(warning => warning.GetString()).Order().ToArray();
            Assert.True(expected.SequenceEqual(warnings.Select(warning => warning.RuleId).Order()), testCase.GetProperty("name").GetString());
            Assert.All(warnings, warning =>
            {
                var confirmed = rules.Single(rule => rule.Id == warning.RuleId).Confirmed;
                Assert.Equal(confirmed ? "warning" : "info", warning.Severity);
                Assert.Equal(confirmed ? "Possible breach \u2014 check against tachograph" : "Guide value \u00b7 rule not yet confirmed", warning.Message);
            });
        }
    }
}
