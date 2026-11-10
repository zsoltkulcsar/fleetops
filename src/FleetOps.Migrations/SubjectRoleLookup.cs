using System.Text.Json;
using FleetOps.Kernel.Authorization;
using Npgsql;

namespace FleetOps.Migrations;

public sealed class EfSubjectRoleLookup : ISubjectRoleLookup
{
    public async Task<IReadOnlySet<RoleCode>?> GetRolesAsync(Guid subjectId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("select roles from fleetops.employees where id = $1 for share", (NpgsqlConnection)context.DbConnection, (NpgsqlTransaction)context.DbTransaction);
        command.Parameters.AddWithValue(subjectId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not string json) return null;
        try
        {
            var names = JsonSerializer.Deserialize<string[]>(json);
            if (names is null || names.Length == 0) return null;
            var roles = new HashSet<RoleCode>();
            foreach (var name in names)
            {
                if (!Enum.TryParse<RoleCode>(name, true, out var role) || !Enum.IsDefined(role)) return null;
                roles.Add(role);
            }
            return roles;
        }
        catch (JsonException) { return null; }
    }
}
