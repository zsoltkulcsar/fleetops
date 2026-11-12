using FleetOps.Kernel.Authorization;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
internal static class NotificationDb
{
    public static FleetOpsDbContext Create(IConfiguration configuration, CommandContext? context = null)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        if (context is null)
            MigrationDbContextOptions.Configure(options, configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        else
            MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        if (context is not null) db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
}
