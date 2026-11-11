using System.Text.Json;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Alerts;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Fleet;
using FleetOps.Modules.Notifications;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class EfVehicleReleaseStore : IVehicleReleaseStore
{
    private static FleetOpsDbContext Open(CommandContext context)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(options, context.DbConnection);
        var db = new FleetOpsDbContext(options.Options);
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }
    public async Task<VehicleReleaseFacts?> LockFactsAsync(Guid vehicleId, DateOnly date, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        var vehicle = await db.Vehicles.FromSqlInterpolated($"SELECT * FROM fleetops.vehicles WHERE id = {vehicleId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (vehicle is null) return null;
        var readiness = await new EfReadinessStore(db).ForVehiclesWithoutOverridesAsync([vehicleId], date, cancellationToken);
        return new(vehicle.Status == VehicleStatus.Active && vehicle.Kind == VehicleKind.Truck, readiness[vehicleId].Causes);
    }
    public async Task<string> ActorNameAsync(Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        return await db.Employees.Where(person => person.Id == actorId).Select(person => person.FirstName + " " + person.LastName).SingleAsync(cancellationToken);
    }
    public async Task SaveAsync(VehicleOverride release, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = Open(context);
        db.VehicleOverrides.Add(release);
        db.AuditEntries.Add(new AuditEntry { Id = Guid.NewGuid(), EntityType = "override", EntityId = release.Id, FieldName = "created", NewValue = JsonSerializer.Serialize(release), ActorId = release.By.ToString(), Reason = release.Reason, CreatedAt = release.At });
        await db.SaveChangesAsync(cancellationToken);

    }
}
