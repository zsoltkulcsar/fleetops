using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Setup;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfSettingsStore(IConfiguration configuration) : ISettings
{
    public async Task<int> GetIntAsync(SettingKey key, CommandContext? context, CancellationToken cancellationToken)
    {
        if (!SettingCatalog.Defaults.ContainsKey(key.Value)) throw new KeyNotFoundException(key.Value);
        await using var db = context is null ? new FleetOpsDbContext(CreateOptions(configuration).Options) : CreateDb(context);
        var value = await db.Settings.AsNoTracking().Where(x => x.Key == key.Value).Select(x => (int?)x.Value).SingleOrDefaultAsync(cancellationToken);
        return value ?? SettingCatalog.DefaultFor(key);
    }

    private static DbContextOptionsBuilder<FleetOpsDbContext> CreateOptions(IConfiguration configuration)
    {
        var builder = new DbContextOptionsBuilder<FleetOpsDbContext>();
        builder.UseNpgsql(configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        return builder;
    }

    private static FleetOpsDbContext CreateDb(CommandContext context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
}

public sealed class Setting
{
    public string Key { get; set; } = "";
    public int Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = "";
}
