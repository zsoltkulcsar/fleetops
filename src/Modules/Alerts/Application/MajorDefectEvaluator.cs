using FleetOps.Modules.Setup;
namespace FleetOps.Modules.Alerts;

public sealed class A08MajorDefectEvaluator(IMajorDefectFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-08";
    public string SubjectType => "defect";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var reportedAt = await facts.OpenUnlinkedMajorReportedAtAsync(subjectId, context.Command, cancellationToken);
        if (reportedAt is null) return [];
        var days = await context.Settings.GetIntAsync(new SettingKey("alerts.a08.days"), context.Command, cancellationToken);
        if (days < 0) throw new InvalidOperationException("Invalid A-08 threshold.");
        return reportedAt.Value < now.AddDays(-days) ? [new("", AlertSeverity.Warning, null)] : [];
    }
}
