using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public interface IReleaseReviewStore
{
    Task AddAsync(Alert alert, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReleaseReviewRecipient>> RecipientsAsync(CommandContext context, CancellationToken cancellationToken);
    Task<Alert?> LockAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(Alert alert, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
