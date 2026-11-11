using FleetOps.Kernel;
namespace FleetOps.Modules.Fleet;
public static class VehicleReleaseEligibilityPolicy
{
    public static VehicleReleaseEligibility Evaluate(VehicleDetail vehicle, IReadOnlyList<ReadinessCause> causes,
        IReadOnlyList<string> releasableTypes, ReleaseBanner? activeOverride)
    {
        var expired = causes.Where(cause => cause.Code == ReadinessCauseCode.DocumentExpired)
            .Select(cause => cause.DocumentType!).Distinct().ToArray();
        var allowed = releasableTypes.Count > 0 && vehicle.Kind == VehicleKind.Truck && vehicle.Status == VehicleStatus.Active &&
            expired.Length > 0 && causes.All(cause => cause.Code == ReadinessCauseCode.DocumentExpired &&
                cause.DocumentType is not ("insurance" or "registration") && releasableTypes.Contains(cause.DocumentType!));
        return new(releasableTypes.Count > 0, allowed, expired, releasableTypes,
            activeOverride is null ? null : new(activeOverride.ReleasedBy, activeOverride.ValidUntil));
    }
}
