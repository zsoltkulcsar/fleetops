namespace FleetOps.Modules.Alerts;
public sealed class CorrectionRequestEvaluator(ICorrectionRequestFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-14";
    public string SubjectType => "correction_request";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var request = await facts.ReadAsync(subjectId, context.Command, cancellationToken);
        return request?.Open == true ? [new(request.RequiresHr ? "hr" : "dispatcher", AlertSeverity.Warning, null)] : [];
    }
}
