namespace FleetOps.Modules.Alerts;
public sealed class A04ExpiryEvaluator(IExpiryFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-04";
    public string SubjectType => "vehicle";
    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var current = await facts.ReadAsync(SubjectType, subjectId, context.Command, cancellationToken);
        if (!current.Eligible) return [];
        var conditions = new List<AlertCondition>();
        foreach (var document in current.Documents)
        {
            var lead = await context.Settings.GetIntAsync(new($"documents.lead_days.{document.Type}"), context.Command, cancellationToken);
            if (document.ExpiresAt >= today && document.ExpiresAt.DayNumber - today.DayNumber <= lead)
                conditions.Add(new(document.Type, AlertSeverity.Warning, document.ExpiresAt));
        }
        if (current.LeaseEndDate is DateOnly leaseEnd)
        {
            var lead = await context.Settings.GetIntAsync(new("vehicles.lease_lead_days"), context.Command, cancellationToken);
            if (leaseEnd >= today && leaseEnd.DayNumber - today.DayNumber <= lead)
                conditions.Add(new("lease_end", AlertSeverity.Warning, leaseEnd));
        }
        return conditions;
    }
}
