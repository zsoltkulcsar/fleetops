using FleetOps.Kernel;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfOfflineCommandResults(TimeProvider clock) : IOfflineCommandResults
{
    public async Task LockAsync(Guid employeeId, CommandContext context, CancellationToken cancellationToken) =>
        await new EfShiftCommandStore().LockEmployeeAsync(employeeId, context, cancellationToken);

    public async Task<StoredCommandResult?> ReadAsync(Guid employeeId, Guid commandId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        return await db.CommandReceipts.AsNoTracking().Where(receipt => receipt.EmployeeId == employeeId && receipt.CommandId == commandId)
            .Select(receipt => new StoredCommandResult(receipt.Type, receipt.Result)).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(Guid employeeId, Guid commandId, Guid aggregateId, string type, string payload, string result, DateTimeOffset? deviceTime, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        db.CommandReceipts.Add(new CommandReceipt { EmployeeId = employeeId, CommandId = commandId, AggregateId = aggregateId,
            Type = type, SchemaVersion = 1, Payload = payload, Result = result, Outcome = "applied", DeviceTime = deviceTime ?? clock.GetUtcNow(), ServerReceivedAt = clock.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }
}
