using FleetOps.Kernel.Authorization;

namespace FleetOps.Modules.Setup;

public readonly record struct SettingKey(string Value);

public interface ISettings
{
    Task<int> GetIntAsync(SettingKey key, CommandContext? context, CancellationToken cancellationToken);
}

public static class SettingCatalog
{
    public static readonly IReadOnlyDictionary<string, int> Defaults = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["alerts.a06.critical_days"] = 14,
        ["alerts.a06.critical_km"] = 2000,
        ["tacho.driver_card_days"] = 28,
        ["tacho.vehicle_unit_days"] = 90,
        ["vehicles.lease_lead_days"] = 30,
        ["enrolment.code_minutes"] = 30,
        ["enrolment.max_tries"] = 5,
        ["driver.pin_length"] = 6,
        ["driver.max_pin_tries"] = 5,
        ["driver.max_offline_days"] = 45,
        ["driver.session_days"] = 180,
        ["company.device_time_gap_minutes"] = 5,
        ["company.payroll_cutoff_day"] = 5,
        ["company.max_shift_hours"] = 0,
        ["timesheets.submit_reminder_hour"] = 7,
        ["alerts.a03.queue_days"] = 2,
        ["alerts.a08.days"] = 3,
        ["alerts.a03.blocked_hours"] = 1,
        ["alerts.escalation_minutes"] = 15,
        ["documents.lead_days.licence_c_ce"] = 60,
        ["documents.lead_days.driver_card"] = 60,
        ["documents.lead_days.code95"] = 90,
        ["documents.lead_days.medical"] = 60,
        ["documents.lead_days.adr_certificate"] = 60,
        ["documents.lead_days.id_card"] = 30,
        ["documents.lead_days.hu_report"] = 30,
        ["documents.lead_days.sp_report"] = 30,
        ["documents.lead_days.tacho_calibration"] = 30,
        ["documents.lead_days.insurance"] = 30,
        ["documents.lead_days.community_licence"] = 60
        , ["notifications.max_attempts"] = 5
    };

    public static int DefaultFor(SettingKey key) => Defaults.TryGetValue(key.Value, out var value) ? value : throw new KeyNotFoundException(key.Value);
}
