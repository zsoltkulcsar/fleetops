using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public sealed class ScheduleQueryHandler(FleetOpsDbContext db, TimeProvider time) : IQueryHandler<GetScheduleQuery, ScheduleResult>
{
    public async Task<ScheduleResult> HandleAsync(GetScheduleQuery query, AccessContext actor, CancellationToken cancellationToken)
    {
        if (query.From == default || query.To < query.From || query.To.DayNumber - query.From.DayNumber > 366) throw new ScheduleValidationException(["from", "to"]);
        var cycles = db.Cycles.Where(cycle => cycle.From <= query.To && cycle.To >= query.From);
        if (query.EmployeeId.HasValue) cycles = cycles.Where(cycle => cycle.EmployeeId == query.EmployeeId);
        if (query.VehicleId.HasValue) cycles = cycles.Where(cycle => cycle.VehicleId == query.VehicleId);
        var swaps = db.Assignments.AsNoTracking().Where(assignment => assignment.From <= query.To && (assignment.To == null || assignment.To >= query.From));
        if (query.EmployeeId.HasValue) swaps = swaps.Where(assignment => assignment.EmployeeId == query.EmployeeId);
        if (query.VehicleId.HasValue) swaps = swaps.Where(assignment => assignment.VehicleId == query.VehicleId);
        var swapRows = actor.Roles.Any(role => role is RoleCode.Dispatcher or RoleCode.Owner)
            ? await swaps.OrderBy(assignment => assignment.From).ThenBy(assignment => assignment.Id)
                .Select(assignment => new ScheduleSwap(assignment.Id, assignment.EmployeeId,
                    db.Employees.Where(employee => employee.Id == assignment.EmployeeId).Select(employee => employee.FirstName + " " + employee.LastName).First(),
                    assignment.VehicleId, db.Vehicles.Where(vehicle => vehicle.Id == assignment.VehicleId).Select(vehicle => vehicle.Plate).First(),
                    assignment.From, assignment.To, assignment.MaintenanceId, "Swap")).ToListAsync(cancellationToken)
            : [];
        var maintenance = db.Maintenance.AsNoTracking().Where(item => db.Vehicles.Any(vehicle => vehicle.Id == item.VehicleId && vehicle.Kind == FleetOps.Modules.Fleet.VehicleKind.Truck) && item.ScheduledFor >= query.From && item.ScheduledFor <= query.To &&
            (item.Status == FleetOps.Modules.Maintenance.MaintenanceStatus.Scheduled || item.Status == FleetOps.Modules.Maintenance.MaintenanceStatus.InProgress));
        if (query.VehicleId.HasValue) maintenance = maintenance.Where(item => item.VehicleId == query.VehicleId);
        if (query.EmployeeId.HasValue) maintenance = maintenance.Where(item => cycles.Any(cycle => cycle.VehicleId == item.VehicleId));
        var maintenanceRows = actor.Roles.Any(role => role is RoleCode.Dispatcher or RoleCode.Owner)
            ? await maintenance.OrderBy(item => item.ScheduledFor).ThenBy(item => item.Id).Select(item => new ScheduleMaintenance(item.Id, item.VehicleId,
                db.Vehicles.Where(vehicle => vehicle.Id == item.VehicleId).Select(vehicle => vehicle.Plate).First(), item.Type, item.ScheduledFor,
                item.Status.ToString(), "Workshop: " + (item.Type == "inspection_hu" ? "HU" : item.Type == "inspection_sp" ? "SP" : item.Type))).ToListAsync(cancellationToken)
            : [];
        return new(time.GetUtcNow(), await ScheduleProjection.ReadAsync(db, cycles, cancellationToken)) { Swaps = swapRows, Maintenance = maintenanceRows };
    }
}

