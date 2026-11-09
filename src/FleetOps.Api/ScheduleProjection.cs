using FleetOps.Kernel.Authorization;
using FleetOps.Migrations;
using FleetOps.Modules.Schedule;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Api;

public static class ScheduleProjection
{
    public static async Task<IReadOnlyList<CycleDetail>> ReadAsync(FleetOpsDbContext db, IQueryable<Cycle> query, CancellationToken cancellationToken)
    {
        var rows = await query.AsNoTracking().OrderBy(cycle => cycle.From).ThenBy(cycle => cycle.Id).ToListAsync(cancellationToken);
        var employeeIds = rows.Select(cycle => cycle.EmployeeId).Distinct().ToArray();
        var vehicleIds = rows.Select(cycle => cycle.VehicleId).Distinct().ToArray();
        var names = await db.Employees.Where(employee => employeeIds.Contains(employee.Id)).ToDictionaryAsync(employee => employee.Id, employee => employee.FirstName + " " + employee.LastName, cancellationToken);
        var plates = await db.Vehicles.Where(vehicle => vehicleIds.Contains(vehicle.Id)).ToDictionaryAsync(vehicle => vehicle.Id, vehicle => vehicle.Plate, cancellationToken);
        var ids = rows.Select(cycle => cycle.Id).ToArray();
        var clashes = await db.Alerts.Where(alert => alert.RuleId == "A-21" && alert.SubjectType == "cycle" && ids.Contains(alert.SubjectId) && alert.Status != FleetOps.Modules.Alerts.AlertStatus.Resolved).Select(alert => alert.SubjectId).ToListAsync(cancellationToken);
        return rows.Select(cycle => new CycleDetail(cycle.Id, cycle.EmployeeId, names.GetValueOrDefault(cycle.EmployeeId, ""), cycle.VehicleId, plates.GetValueOrDefault(cycle.VehicleId, ""), cycle.From, cycle.To, cycle.Status, cycle.Source, cycle.PatternSlot, cycle.Version, clashes.Contains(cycle.Id))).ToArray();
    }
}

