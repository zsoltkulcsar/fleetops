using FleetOps.Kernel;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Maintenance;
using FleetOps.Modules.Schedule;
using FleetOps.Modules.Shifts;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;

public sealed class EfRepairReads(FleetOpsDbContext db, ICompanyTimeService companyTime, TimeProvider time) : IRepairReads
{
    public async Task<MaintenanceList> ListAsync(ListMaintenanceQuery query, CancellationToken cancellationToken)
    {
        var repairs = db.Maintenance.AsNoTracking().AsQueryable();
        if (query.VehicleId.HasValue) repairs = repairs.Where(repair => repair.VehicleId == query.VehicleId);
        if (query.Status.HasValue) repairs = repairs.Where(repair => repair.Status == query.Status);
        if (query.From.HasValue) repairs = repairs.Where(repair => repair.ScheduledFor >= query.From);
        if (query.To.HasValue) repairs = repairs.Where(repair => repair.ScheduledFor <= query.To);
        var rows = await repairs.OrderBy(repair => repair.ScheduledFor).ThenBy(repair => repair.Id).ToListAsync(cancellationToken);
        return new(rows.Select(repair => new RepairDetail(repair.Id, repair.Type, repair.VehicleId, repair.Workshop,
            repair.ScheduledFor, repair.DefectReportIds, repair.Status, repair.CancelReason, repair.DoneAt,
            repair.OdometerAtDoneKm, repair.Status == MaintenanceStatus.InProgress)).ToArray());
    }
    public async Task<SwapOptions> OptionsAsync(SwapOptionsQuery query, CancellationToken cancellationToken)
    {
        var trucks = await db.Vehicles.AsNoTracking().Where(vehicle => vehicle.Kind == VehicleKind.Truck).OrderBy(vehicle => vehicle.Plate).ToListAsync(cancellationToken);
        var ids = trucks.Select(vehicle => vehicle.Id).ToArray();
        var readiness = await new EfReadinessStore(db).ForVehiclesAsync(ids, query.Date, cancellationToken);
        var today = companyTime.ToCompanyDate(time.GetUtcNow());
        var cycles = await db.Cycles.AsNoTracking().Where(cycle => ids.Contains(cycle.VehicleId) && cycle.From <= today &&
            (cycle.To >= today || cycle.Status == CycleStatus.Overrun) && cycle.Status != CycleStatus.Cancelled && cycle.Status != CycleStatus.Done)
            .OrderByDescending(cycle => cycle.From).ThenByDescending(cycle => cycle.Id).ToListAsync(cancellationToken);
        var shifts = await db.Shifts.AsNoTracking().Where(shift => shift.TruckId != null && ids.Contains(shift.TruckId.Value) &&
            shift.Status == ShiftStatus.Running && shift.EndAt == null).OrderBy(shift => shift.StartAt).ToListAsync(cancellationToken);
        var employeeIds = cycles.Select(cycle => cycle.EmployeeId).Concat(shifts.Select(shift => shift.EmployeeId)).Distinct().ToArray();
        var names = await db.Employees.Where(employee => employeeIds.Contains(employee.Id))
            .ToDictionaryAsync(employee => employee.Id, employee => employee.FirstName + " " + employee.LastName, cancellationToken);
        return new(trucks.Where(truck => readiness.GetValueOrDefault(truck.Id)?.Usable == true).Select(truck =>
        {
            var owner = cycles.FirstOrDefault(cycle => cycle.VehicleId == truck.Id);
            var running = shifts.FirstOrDefault(shift => shift.TruckId == truck.Id);
            return new SwapOption(truck.Id, truck.Plate, owner is null ? null : names.GetValueOrDefault(owner.EmployeeId),
                running is null ? null : names.GetValueOrDefault(running.EmployeeId), running is null ? null : companyTime.ToCompanyTime(running.StartAt));
        }).ToArray());
    }
}
