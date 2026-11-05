using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public static class MigrationDbContextOptions
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "fleetops"));
    }
}
