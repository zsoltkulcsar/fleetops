namespace FleetOps.Api.Pipeline;

public static class DatabaseConnectionString
{
    public static string? Resolve(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FleetOps");
        var fallback = configuration["FLEETOPS_DATABASE_RUNTIME_URL"];
        return string.IsNullOrWhiteSpace(connectionString)
            ? (string.IsNullOrWhiteSpace(fallback) ? null : fallback)
            : connectionString;
    }
}
