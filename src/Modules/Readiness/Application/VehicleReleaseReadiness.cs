using FleetOps.Kernel;
using FleetOps.Modules.Fleet;
namespace FleetOps.Modules.Readiness;
public static class VehicleReleaseReadiness
{
    public static VehicleReadiness Apply(VehicleReadiness readiness, VehicleOverride? release)
    {
        if (release is null) return readiness;
        var causes = readiness.Causes.Where(cause => cause.Code != ReadinessCauseCode.DocumentExpired || cause.DocumentType is "insurance" or "registration" || !release.DocTypes.Contains(cause.DocumentType!)).ToArray();
        return new(causes.Length == 0, causes.Length == 0 ? ReadinessShownStatus.Active : readiness.ShownStatus, causes);
    }
}
