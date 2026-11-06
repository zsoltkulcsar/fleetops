using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using System.Data.Common;

namespace FleetOps.Migrations;

public sealed class RetiredVehicleEditPrecondition : ICommandStatePrecondition<EditVehicleCommand>
{
    public async Task<bool> CheckAsync(EditVehicleCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var sql = context.DbConnection.CreateCommand();
        sql.Transaction = context.DbTransaction;
        sql.CommandText = "select status from fleetops.vehicles where id = @id";
        var parameter = sql.CreateParameter(); parameter.ParameterName = "id"; parameter.Value = command.SubjectId ?? Guid.Empty; sql.Parameters.Add(parameter);
        var status = await sql.ExecuteScalarAsync(cancellationToken);
        return status is not string text || !text.Equals("Retired", StringComparison.OrdinalIgnoreCase);
    }
}
