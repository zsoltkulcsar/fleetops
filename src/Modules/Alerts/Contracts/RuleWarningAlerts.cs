using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public interface IRuleWarningAlerts
{
    Task UpdateAsync(Guid shiftId, IReadOnlyList<string> ruleIds, bool confirmed, bool newWarning, CommandContext context, CancellationToken cancellationToken);
}
[CommandPolicy("alerts.rules.acknowledge", HasStatePrecondition = true)]
public sealed record AcknowledgeRuleWarningCommand(Guid AlertId, int Version, string Note) : ICommand<ReleaseReviewResult>;
