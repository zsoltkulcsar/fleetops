using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public sealed class AcknowledgeAbsenceHandler(IReleaseReviewStore store, TimeProvider time) : ICommandHandler<AcknowledgeAbsenceCommand, ReleaseReviewResult>
{
    public async Task<ReleaseReviewResult> HandleAsync(AcknowledgeAbsenceCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        var alert = await store.LockAsync(command.AlertId, context, cancellationToken) ?? throw new ReleaseReviewConflictException("not_found");
        if (alert.RuleId != "A-18") throw new ReleaseReviewConflictException("not_found");
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
