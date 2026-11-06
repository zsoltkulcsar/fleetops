using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FleetOps.Migrations;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<FleetOpsDbContext>
{
    public FleetOpsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        options.UseNpgsql();
        return new FleetOpsDbContext(options.Options);
    }

    public static string ResolveConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEETOPS_DATABASE_MIGRATION_URL")
            ?? throw new InvalidOperationException("FLEETOPS_DATABASE_MIGRATION_URL is required.");
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(builder.RootCertificate) && !Path.IsPathRooted(builder.RootCertificate))
        {
            builder.RootCertificate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), builder.RootCertificate));
        }

        return builder.ConnectionString;
    }
}
