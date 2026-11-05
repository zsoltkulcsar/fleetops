using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FleetOps.Migrations;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<FleetOpsDbContext>
{
    public FleetOpsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEETOPS_DATABASE_MIGRATION_URL")
            ?? "Host=localhost;Port=5432;Database=fleetops;Username=postgres;Password=postgres";
        var builder = new Npgsql.NpgsqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(builder.RootCertificate) && !Path.IsPathRooted(builder.RootCertificate))
            builder.RootCertificate = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), builder.RootCertificate));
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(
            builder.ConnectionString).Options;
        return new FleetOpsDbContext(options);
    }
}
