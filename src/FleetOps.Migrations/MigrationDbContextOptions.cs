using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace FleetOps.Migrations;

public static class MigrationDbContextOptions
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "fleetops"));
        options.AddInterceptors(new AuditSaveChangesInterceptor());
    }

    public static void ConfigureRuntime(DbContextOptionsBuilder options, DbConnection connection) => options.UseNpgsql(connection).AddInterceptors(new AuditSaveChangesInterceptor());
}
