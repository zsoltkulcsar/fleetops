using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public static class MigrationsProgram
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddDbContext<FleetOpsDbContext>(options =>
            MigrationDbContextOptions.Configure(options,
                Environment.GetEnvironmentVariable("FLEETOPS_DATABASE_MIGRATION_URL") ??
                "Host=localhost;Port=5432;Database=fleetops;Username=postgres;Password=postgres"));
        var app = builder.Build();
        app.MapGet("/", () => "FleetOps migrations");
        app.Run();
    }
}
