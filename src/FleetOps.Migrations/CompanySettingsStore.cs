using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Setup;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfCompanySettingsStore(IConfiguration configuration, TimeProvider time) : ICompanySettingsStore, IReleaseSettings
{
    private FleetOpsDbContext Open(CommandContext? context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        if (context is null) MigrationDbContextOptions.Configure(options, configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        else MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        if (context is not null) db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
    public async Task<SettingsView> ReadAsync(CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var values = SettingCatalog.Defaults.Where(pair => SettingsHandler.Range(pair.Key) is not null).ToDictionary();
        foreach (var setting in await db.Settings.AsNoTracking().ToListAsync(cancellationToken))
            if (values.ContainsKey(setting.Key)) values[setting.Key] = setting.Value;
        var holidays = await db.CompanyHolidays.AsNoTracking().OrderBy(holiday => holiday.Date).Select(holiday => new HolidayView(holiday.Date, holiday.Name)).ToArrayAsync(cancellationToken);
        return new(values, await GetReleasableDocTypesAsync(context, cancellationToken), holidays);
    }
    public async Task<IReadOnlyList<string>> GetReleasableDocTypesAsync(CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var value = await db.SettingLists.AsNoTracking().Where(setting => setting.Key == "company.releasable_doc_types").Select(setting => setting.Values).SingleOrDefaultAsync(cancellationToken);
        return value ?? [];
    }
    public async Task SaveAsync(IReadOnlyDictionary<string, int> values, string[]? releasableDocTypes, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(700078)", cancellationToken);
        foreach (var value in values)
        {
            var setting = await db.Settings.FindAsync([value.Key], cancellationToken);
            var old = setting?.Value ?? SettingCatalog.DefaultFor(new(value.Key));
            if (old == value.Value) continue;
            if (setting is null)
            {
                setting = new Setting { Key = value.Key };
                db.Settings.Add(setting);
            }
            setting.Value = value.Value;
            setting.UpdatedAt = time.GetUtcNow();
            setting.UpdatedBy = actor.EmployeeId.ToString();
            Audit(db, value.Key, old.ToString(System.Globalization.CultureInfo.InvariantCulture), value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), actor);
        }
        if (releasableDocTypes is not null)
        {
            const string key = "company.releasable_doc_types";
            var setting = await db.SettingLists.FindAsync([key], cancellationToken);
            var old = setting?.Values ?? [];
            if (!old.Order().SequenceEqual(releasableDocTypes.Order()))
            {
                if (setting is null)
                {
                    setting = new SettingList { Key = key };
                    db.SettingLists.Add(setting);
                }
                setting.Values = releasableDocTypes;
                Audit(db, key, JsonSerializer.Serialize(old), JsonSerializer.Serialize(releasableDocTypes), actor);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task SetHolidayAsync(DateOnly date, string? name, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(700078)", cancellationToken);
        var holiday = await db.CompanyHolidays.FindAsync([date], cancellationToken);
        var old = holiday?.Name;
        if (old == name) return;
        if (name is null)
        {
            if (holiday is not null) db.CompanyHolidays.Remove(holiday);
        }
        else if (holiday is null) db.CompanyHolidays.Add(new CompanyHoliday { Date = date, Name = name });
        else holiday.Name = name;
        Audit(db, "holidays." + date.ToString("yyyy-MM-dd"), old, name, actor);
        await db.SaveChangesAsync(cancellationToken);
    }
    private void Audit(FleetOpsDbContext db, string key, string? old, string? value, AccessContext actor) => db.AuditEntries.Add(new AuditEntry
    {
        Id = Guid.NewGuid(), EntityType = "setting", EntityId = Guid.Empty, FieldName = key, OldValue = old,
        NewValue = value, ActorId = actor.EmployeeId.ToString(), Reason = "settings_updated", CreatedAt = time.GetUtcNow()
    });
}
