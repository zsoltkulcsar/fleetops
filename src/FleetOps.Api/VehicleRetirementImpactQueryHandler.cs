using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class VehicleRetirementImpactQueryHandler(FleetOpsDbContext db, FleetOps.Kernel.ICompanyTimeService companyTime, TimeProvider time) : IQueryHandler<GetVehicleRetirementImpactQuery, VehicleRetirementImpact>
{
    public async Task<VehicleRetirementImpact> HandleAsync(GetVehicleRetirementImpactQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        return new(await ScheduleProjection.ReadAsync(db, db.Cycles.Where(cycle => cycle.VehicleId == query.VehicleId && cycle.To >= today && cycle.Status != CycleStatus.Done && cycle.Status != CycleStatus.Cancelled), cancellationToken));
    }
}
