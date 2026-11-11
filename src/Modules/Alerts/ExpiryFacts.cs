using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Alerts;
public sealed record ExpiryDocument(string Type, DateOnly ExpiresAt);
public sealed record ExpiryFacts(bool Eligible, IReadOnlyList<ExpiryDocument> Documents, DateOnly? LeaseEndDate);
public interface IExpiryFacts
{
    Task<ExpiryFacts> ReadAsync(string subjectType, Guid subjectId, CommandContext context, CancellationToken cancellationToken);
}
