using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Fleet;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;

public sealed class EfTachoDownloadFacts : ITachoDownloadFacts
{
    public async Task<TachoDownloadFact> ReadAsync(string subjectType, Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        if (subjectType == "vehicle")
        {
            var truck = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(vehicle => vehicle.Id == id, cancellationToken);
            return new(truck is { Kind: VehicleKind.Truck, Status: VehicleStatus.Active }, truck?.LastTachoDownloadAt);
        }
        var person = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == id, cancellationToken);
        return new(person is { Status: EmployeeStatus.Active } && (JsonSerializer.Deserialize<string[]>(person.Roles) ?? []).Contains("driver", StringComparer.OrdinalIgnoreCase), person?.LastTachoDownloadAt);
    }
}
