namespace FleetOps.Modules.Alerts;

public abstract class TachoDownloadEvaluator(ITachoDownloadFacts facts) : IAlertEvaluator
{
    public string RuleId => "A-12";
    public abstract string SubjectType { get; }
    protected abstract string Setting { get; }

    public async Task<IReadOnlyList<AlertCondition>> EvaluateAsync(Guid subjectId, DateOnly today, DateTimeOffset now, EvaluationContext context, CancellationToken cancellationToken)
    {
        var fact = await facts.ReadAsync(SubjectType, subjectId, context.Command, cancellationToken);
        if (!fact.Eligible) return [];
        var interval = await context.Settings.GetIntAsync(new(Setting), context.Command, cancellationToken);
        if (fact.LastDownload is null) return [new("no_download_logged", AlertSeverity.Warning, null)];
        var due = fact.LastDownload.Value.AddDays(interval);
        return today > due ? [new("download_due", AlertSeverity.Warning, due)] : [];
    }
}
