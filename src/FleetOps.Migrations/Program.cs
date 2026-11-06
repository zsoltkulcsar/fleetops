using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public static class MigrationsProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddDbContext<FleetOpsDbContext>(options => ConfigureMigrationOptions(options));
        var app = builder.Build();
        app.MapGet("/", () => "FleetOps migrations");
        app.Run();
    }

    private static void ConfigureMigrationOptions(DbContextOptionsBuilder options)
    {
        var connectionString = Environment.GetEnvironmentVariable("FLEETOPS_DATABASE_MIGRATION_URL");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("FLEETOPS_DATABASE_MIGRATION_URL is required.");
        MigrationDbContextOptions.Configure(options, connectionString);
    }
}
