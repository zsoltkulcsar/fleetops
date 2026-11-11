using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Documents;
using FleetOps.Modules.Fleet;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfExpiryFacts : IExpiryFacts
{
    public async Task<ExpiryFacts> ReadAsync(string subjectType, Guid subjectId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        DateOnly? leaseEnd = null;
        if (subjectType == "employee")
        {
            var employee = await db.Employees.AsNoTracking().SingleOrDefaultAsync(employee => employee.Id == subjectId, cancellationToken);
            if (employee is null || employee.Status == EmployeeStatus.Left || !(JsonSerializer.Deserialize<string[]>(employee.Roles) ?? []).Contains("driver")) return new(false, [], null);
        }
        else
        {
            var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(vehicle => vehicle.Id == subjectId, cancellationToken);
            if (vehicle is null || vehicle.Status != VehicleStatus.Active) return new(false, [], null);
            if (vehicle.Ownership is VehicleOwnership.Leased or VehicleOwnership.RentedIn) leaseEnd = vehicle.LeaseEndDate;
        }
        var ownerType = subjectType == "employee" ? DocumentOwnerType.Employee : DocumentOwnerType.Vehicle;
        var documents = await db.Documents.AsNoTracking().Where(document => document.OwnerType == ownerType && document.OwnerId == subjectId && document.Status == DocumentStatus.Verified).ToArrayAsync(cancellationToken);
        var current = documents.GroupBy(document => document.Type).Select(group => group.OrderByDescending(document => document.Version).ThenByDescending(document => document.CreatedAt).ThenByDescending(document => document.Id).First());
        return new(true, current.Where(document => document.ExpiresAt.HasValue).Select(document => new ExpiryDocument(document.Type == DocumentType.LicenceC_CE ? "licence_c_ce" : document.Type.ToString().ToSnakeCase(), document.ExpiresAt!.Value)).ToArray(), leaseEnd);
    }
}
