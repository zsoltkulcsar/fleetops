using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record SettingsView(IReadOnlyDictionary<string, int> Values, IReadOnlyList<string> ReleasableDocTypes, IReadOnlyList<HolidayView> Holidays) : IReadModel
{
    public IReadOnlyDictionary<string, string> Guidance { get; init; } = new Dictionary<string, string>
    {
        ["tacho.driver_card_days"] = "Guide value \u00b7 rule not yet confirmed",
        ["tacho.vehicle_unit_days"] = "Guide value \u00b7 rule not yet confirmed"
    };
}
