using FleetOps.Kernel.Authorization;

namespace FleetOps.Api.Pipeline;

public interface ICommandTransaction : IAsyncDisposable
{
    CommandContext Context { get; }
    Task CommitAsync(CancellationToken cancellationToken);
    Task RollbackAsync(CancellationToken cancellationToken);
    new ValueTask DisposeAsync();
}

public interface ICommandTransactionFactory
{
    Task<ICommandTransaction> BeginAsync(CancellationToken cancellationToken);
}
