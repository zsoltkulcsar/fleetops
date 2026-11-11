using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public sealed class AcknowledgeRuleWarningHandler(IReleaseReviewStore reviews, IRuleWarningAlertStore store, TimeProvider clock) : ICommandHandler<AcknowledgeRuleWarningCommand, ReleaseReviewResult>
{
    public async Task<ReleaseReviewResult> HandleAsync(AcknowledgeRuleWarningCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (!actor.Roles.Any(role => role is RoleCode.Dispatcher or RoleCode.Hr or RoleCode.Owner))
            throw new AuthorizationException("permission_denied", "alerts.rules.acknowledge", [RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner]);
        if (string.IsNullOrWhiteSpace(command.Note) || command.Note.Length > 500) throw new FleetOps.Modules.Setup.SettingsValidationException(["note"]);
        var alert = await reviews.LockAsync(command.AlertId, context, cancellationToken) ?? throw new ReleaseReviewConflictException("not_found");
        if (alert.RuleId != "A-11") throw new ReleaseReviewConflictException("not_found");
        if (alert.Version != command.Version) throw new ReleaseReviewConflictException("version_conflict");
        if (alert.Status == AlertStatus.Resolved) throw new ReleaseReviewConflictException("state_conflict");
        alert.Status = AlertStatus.Resolved;
        alert.ResolvedBy = actor.EmployeeId;
        alert.ResolvedAt = clock.GetUtcNow();
        alert.Version++;
        await store.SaveAsync(alert, command.Note.Trim(), actor.EmployeeId, context, cancellationToken);
        return new(alert.Id, "resolved", alert.Version);
    }
}
