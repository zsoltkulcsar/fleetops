using FleetOps.Kernel.Authorization;

namespace FleetOps.Kernel;

public sealed record StoredCommandResult(string Type, string Result);
public interface IOfflineCommandResults
{
    Task LockAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken);
    Task<StoredCommandResult?> ReadAsync(Guid employeeId, Guid commandId, CommandContext context, CancellationToken cancellationToken);
    Task SaveAsync(Guid employeeId, Guid commandId, Guid aggregateId, string type, string payload, string result, DateTimeOffset? deviceTime, CommandContext context, CancellationToken cancellationToken);
}
