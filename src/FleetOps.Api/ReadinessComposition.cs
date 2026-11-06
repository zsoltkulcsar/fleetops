using FleetOps.Kernel;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Readiness;

namespace FleetOps.Api;

public static class ReadinessComposition
{
    public static async Task<VehicleDetail> ComposeAsync(this VehicleDetail detail, IReadiness readiness, ICompanyTimeService companyTime, TimeProvider time, CancellationToken cancellationToken)
    {
        var result = await readiness.ForVehiclesAsync([detail.Id], companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken);
        return result.TryGetValue(detail.Id, out var value) ? detail with { ShownStatus = value.ShownStatus, Usable = value.Usable, Causes = value.Causes } : detail;
    }

    public static async Task<VehicleListResult> ComposeAsync(this VehicleListResult result, IReadiness readiness, ICompanyTimeService companyTime, TimeProvider time, CancellationToken cancellationToken)
    {
        var values = await readiness.ForVehiclesAsync(result.Items.Select(x => x.Id).ToArray(), companyTime.ToCompanyDate(time.GetUtcNow()), cancellationToken);
        return result with { Items = result.Items.Select(item => values.TryGetValue(item.Id, out var value) ? item with { ShownStatus = value.ShownStatus, Usable = value.Usable, Causes = value.Causes } : item).ToList() };
    }
}
