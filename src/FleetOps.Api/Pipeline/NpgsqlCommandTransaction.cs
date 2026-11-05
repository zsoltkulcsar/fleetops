using FleetOps.Kernel.Authorization;
using Npgsql;

namespace FleetOps.Api.Pipeline;

public sealed class NpgsqlCommandTransactionFactory(IConfiguration configuration) : ICommandTransactionFactory
{
    public async Task<ICommandTransaction> BeginAsync(CancellationToken cancellationToken)
    {
        var connectionString = DatabaseConnectionString.Resolve(configuration)
            ?? throw new InvalidOperationException("The FleetOps database connection string is not configured.");
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return new NpgsqlCommandTransaction(connection, await connection.BeginTransactionAsync(cancellationToken));
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

public sealed class NpgsqlCommandTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction) : ICommandTransaction
{
    public CommandContext Context { get; } = new(connection, transaction);
    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
    public Task RollbackAsync(CancellationToken cancellationToken) => transaction.RollbackAsync(cancellationToken);
    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
