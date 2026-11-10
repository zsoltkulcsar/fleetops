using FleetOps.Modules.Shifts;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfTimesheetDriverStore(FleetOpsDbContext db) : ITimesheetDriverStore
{
    public async Task<TimesheetDrivers> FindAsync(string? search, Guid? id, CancellationToken cancellationToken)
    {
        var employees = db.Employees.AsNoTracking().Where(employee => employee.Roles.Contains("\"driver\"") && employee.Status != EmployeeStatus.Left);
        if (id.HasValue) employees = employees.Where(employee => employee.Id == id.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.ToLowerInvariant();
            employees = employees.Where(employee => (employee.FirstName + " " + employee.LastName).ToLower().Contains(term) ||
                (employee.PersonnelNumber != null && employee.PersonnelNumber.ToLower().Contains(term)));
        }
        return new(await employees.OrderBy(employee => employee.LastName).ThenBy(employee => employee.FirstName).ThenBy(employee => employee.Id)
            .Take(20).Select(employee => new TimesheetDriver(employee.Id, employee.FirstName + " " + employee.LastName, employee.PersonnelNumber)).ToListAsync(cancellationToken));
    }
}
