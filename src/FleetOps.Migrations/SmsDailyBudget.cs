using FleetOps.Identity;
using Npgsql;
namespace FleetOps.Migrations;
public sealed class SmsDailyBudget(IConfiguration configuration) : ISmsDailyBudget
{
    public async Task<bool> TryReserveAsync(DateOnly date, int limit, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("insert into fleetops.sms_daily_usage (date, count) values ($1, 1) on conflict (date) do update set count = sms_daily_usage.count + 1 where sms_daily_usage.count < $2 returning count", connection);
        command.Parameters.AddWithValue(date);
        command.Parameters.AddWithValue(limit);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }
}
