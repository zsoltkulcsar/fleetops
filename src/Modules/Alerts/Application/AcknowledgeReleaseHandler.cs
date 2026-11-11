using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public sealed class AcknowledgeReleaseHandler(IReleaseReviewStore store, TimeProvider time) : ICommandHandler<AcknowledgeReleaseCommand, ReleaseReviewResult>
{
    public async Task<ReleaseReviewResult> HandleAsync(AcknowledgeReleaseCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var alert = await store.LockAsync(command.AlertId, context, cancellationToken) ?? throw new ReleaseReviewConflictException("not_found");
        if (alert.RuleId != "A-13") throw new ReleaseReviewConflictException("not_found");
        if (alert.Version != command.Version) throw new ReleaseReviewConflictException("version_conflict");
        if (alert.Status == AlertStatus.Resolved) throw new ReleaseReviewConflictException("state_conflict");
        alert.Status = AlertStatus.Resolved;
        alert.ResolvedAt = time.GetUtcNow();
        alert.ResolvedBy = actor.EmployeeId;
        alert.Version++;
        await store.SaveAsync(alert, actor, context, cancellationToken);
        return new(alert.Id, "resolved", alert.Version);
    }
}
