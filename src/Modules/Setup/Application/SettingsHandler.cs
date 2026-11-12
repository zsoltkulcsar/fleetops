using System.Text.Json;
using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
public sealed class SettingsHandler(ICompanySettingsStore store) : IQueryHandler<GetSettingsQuery, SettingsView>,
    ICommandHandler<UpdateSettingsCommand, SettingsView>, ICommandHandler<AddHolidayCommand, SettingsView>, ICommandHandler<RemoveHolidayCommand, SettingsView>
{
    public static readonly string[] AllowedReleaseTypes = ["hu_report", "sp_report", "tacho_calibration", "community_licence"];
    public Task<SettingsView> HandleAsync(GetSettingsQuery query, AccessContext actor, CancellationToken cancellationToken) => store.ReadAsync(null, cancellationToken);
    public async Task<SettingsView> HandleAsync(UpdateSettingsCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.Changes is null) throw new SettingsValidationException(["changes"]);
        var values = new Dictionary<string, int>();
        string[]? release = null;
        var errors = new List<string>();
        foreach (var change in command.Changes)
        {
            if (!actor.Roles.Contains(RoleCode.Owner) && change.Key != "company.payroll_cutoff_day")
                throw new AuthorizationException("permission_denied", "settings.manage", [RoleCode.Owner]);
            if (change.Key == "company.releasable_doc_types")
            {
                if (change.Value.ValueKind != JsonValueKind.Array || change.Value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
                {
                    errors.Add(change.Key);
                    continue;
                }
                release = change.Value.EnumerateArray().Select(item => item.GetString()!).ToArray();
                if (release.Any(type => !AllowedReleaseTypes.Contains(type)) || release.Distinct().Count() != release.Length) errors.Add(change.Key);
                continue;
            }
            var range = Range(change.Key);
            if (range is null || change.Value.ValueKind != JsonValueKind.Number || !change.Value.TryGetInt32(out var value) || value < range.Value.Min || value > range.Value.Max)
                errors.Add(change.Key);
            else values.Add(change.Key, value);
        }
        if (errors.Count > 0) throw new SettingsValidationException(errors);
        await store.SaveAsync(values, release, actor, context, cancellationToken);
        return await store.ReadAsync(context, cancellationToken);
    }
    public async Task<SettingsView> HandleAsync(AddHolidayCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.Date == default) throw new SettingsValidationException(["date"]);
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Length > 200 || command.Name.Any(char.IsControl)) throw new SettingsValidationException(["name"]);
        await store.SetHolidayAsync(command.Date, command.Name.Trim(), actor, context, cancellationToken);
        return await store.ReadAsync(context, cancellationToken);
    }
    public async Task<SettingsView> HandleAsync(RemoveHolidayCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        if (command.Date == default) throw new SettingsValidationException(["date"]);
        await store.SetHolidayAsync(command.Date, null, actor, context, cancellationToken);
        return await store.ReadAsync(context, cancellationToken);
    }
    public static (int Min, int Max)? Range(string key) => key switch
    {
        "sms.daily_limit" => (0, 100000),
        "notifications.max_attempts" => (1, 20),
        "company.payroll_cutoff_day" => (1, 28),
        "company.max_shift_hours" => (0, 24),
        "alerts.escalation_minutes" => (1, 1440),
        "alerts.a03.blocked_hours" => (1, 168),
        "alerts.a03.queue_days" or "alerts.a08.days" => (1, 365),
        "alerts.a06.critical_days" => (0, 3650),
        "alerts.a06.critical_km" => (0, 1000000),
        "tacho.driver_card_days" or "tacho.vehicle_unit_days" => (1, 3650),
        "vehicles.lease_lead_days" => (0, 365),
        _ when key.StartsWith("documents.lead_days.", StringComparison.Ordinal) && SettingCatalog.Defaults.ContainsKey(key) => (0, 365),
        _ => null
    };
}
