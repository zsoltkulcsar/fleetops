using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
public interface ICompanySettingsStore
{
    Task<SettingsView> ReadAsync(CommandContext? context, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyDictionary<string, int> values, string[]? releasableDocTypes, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task SetHolidayAsync(DateOnly date, string? name, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
