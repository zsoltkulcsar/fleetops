using Npgsql;

namespace FleetOps.Api.Pipeline;

/// <summary>
/// Caps the Npgsql pool for every reader of the runtime connection string. Without a cap Npgsql allows 100
/// connections per instance; on 2026-11-12 one Demo page burst exhausted the hosted pooler and every request
/// failed until idle connections closed. A string that already names a pool size keeps it.
/// </summary>
public static class DatabasePoolLimits
{
    private static readonly string[] Keys = ["ConnectionStrings:FleetOps", "FLEETOPS_DATABASE_RUNTIME_URL"];

    public static void Apply(IConfiguration configuration)
    {
        var maximum = configuration.GetValue("Database:MaxPoolSize", 10);
        var idleSeconds = configuration.GetValue("Database:ConnectionIdleSeconds", 60);
        foreach (var key in Keys)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value)) continue;
            NpgsqlConnectionStringBuilder builder;
            try
            {
                builder = new NpgsqlConnectionStringBuilder(value);
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (!Names(value, "Maximum Pool Size", "Max Pool Size", "MaxPoolSize")) builder.MaxPoolSize = maximum;
            if (!Names(value, "Connection Idle Lifetime", "ConnectionIdleLifetime")) builder.ConnectionIdleLifetime = idleSeconds;
            configuration[key] = builder.ConnectionString;
        }
    }

    private static bool Names(string value, params string[] names) =>
        names.Any(name => value.Contains(name + "=", StringComparison.OrdinalIgnoreCase) || value.Contains(name + " =", StringComparison.OrdinalIgnoreCase));
}
