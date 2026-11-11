using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Setup;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record SettingsView(IReadOnlyDictionary<string, int> Values, IReadOnlyList<string> ReleasableDocTypes, IReadOnlyList<HolidayView> Holidays) : IReadModel;
