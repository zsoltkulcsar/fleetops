using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetOps.Migrations;

public sealed class EfVehicleStore : IVehicleStore
{
    public async Task<VehicleDetail> CreateAsync(CreateVehicleCommand input, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actor.EmployeeId, input.Reason);
        if (await db.Vehicles.AnyAsync(x => x.Status != VehicleStatus.Retired && (x.Plate == input.Plate || x.Vin == input.Vin), cancellationToken))
            throw new VehicleConflictException("duplicate_vehicle");
        var depot = await db.Depots.SingleOrDefaultAsync(cancellationToken) ?? new FleetOps.Modules.Setup.Depot { Id = Guid.CreateVersion7() };
        if (db.Entry(depot).State == EntityState.Detached) db.Depots.Add(depot);
        var vehicle = new Vehicle { Kind = input.Kind, Plate = input.Plate, Vin = input.Vin, Make = input.Make, Model = input.Model,
            Year = input.Year, GrossWeightKg = input.GrossWeightKg, RequiredLicenceClass = input.RequiredLicenceClass, Ownership = input.Ownership,
            LeaseEndDate = input.LeaseEndDate, TrailerType = input.TrailerType, OdometerKm = input.OdometerKm, DepotId = input.DepotId ?? depot.Id,
            ServiceIntervalKm = input.ServiceIntervalKm, ServiceIntervalMonths = input.ServiceIntervalMonths, LastTachoDownloadAt = input.LastTachoDownloadAt };
        db.Vehicles.Add(vehicle);
        await db.SaveChangesAsync(cancellationToken);
        return await Read(db, vehicle.Id, cancellationToken);
    }

    public async Task<VehicleDetail> EditAsync(EditVehicleCommand input, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actor.EmployeeId, input.Reason);
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.Id == input.SubjectId, cancellationToken) ?? throw new VehicleConflictException("vehicle_not_found");
        if (vehicle.Version != input.Version) throw new VehicleConflictException("version_conflict");
        if (vehicle.Plate != input.Plate && string.IsNullOrWhiteSpace(input.Reason)) throw new VehicleValidationException(["reason"]);
        if (await db.Vehicles.AnyAsync(x => x.Id != vehicle.Id && x.Status != VehicleStatus.Retired && (x.Plate == input.Plate || x.Vin == input.Vin), cancellationToken))
            throw new VehicleConflictException("duplicate_vehicle");
        if (vehicle.Plate != input.Plate) db.PlateHistories.Add(new PlateHistory { VehicleId = vehicle.Id, Plate = vehicle.Plate });
        vehicle.Kind = input.Kind; vehicle.Plate = input.Plate; vehicle.Vin = input.Vin; vehicle.Make = input.Make; vehicle.Model = input.Model;
        vehicle.Year = input.Year; vehicle.GrossWeightKg = input.GrossWeightKg; vehicle.RequiredLicenceClass = input.RequiredLicenceClass;
        vehicle.Ownership = input.Ownership; vehicle.LeaseEndDate = input.LeaseEndDate; vehicle.TrailerType = input.TrailerType; vehicle.OdometerKm = input.OdometerKm;
        vehicle.DepotId = input.DepotId ?? vehicle.DepotId; vehicle.ServiceIntervalKm = input.ServiceIntervalKm; vehicle.ServiceIntervalMonths = input.ServiceIntervalMonths;
        vehicle.LastTachoDownloadAt = input.LastTachoDownloadAt; vehicle.Version++; vehicle.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await Read(db, vehicle.Id, cancellationToken);
    }

    private static FleetOpsDbContext CreateDb(CommandContext context, Guid actorId, string reason)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(context.DbConnection).AddInterceptors(new AuditSaveChangesInterceptor()).Options;
        var db = new FleetOpsDbContext(options) { AuditActorId = actorId, AuditReason = reason };
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }

    internal static async Task<VehicleDetail> Read(FleetOpsDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles.SingleAsync(x => x.Id == id, cancellationToken);
        var history = await db.PlateHistories.Where(x => x.VehicleId == id).OrderBy(x => x.ChangedAt).Select(x => x.Plate).ToListAsync(cancellationToken);
        return new(vehicle.Id, vehicle.Kind, vehicle.Plate, vehicle.Vin, vehicle.Make, vehicle.Model, vehicle.Year, vehicle.GrossWeightKg,
            vehicle.Ownership, vehicle.LeaseEndDate, vehicle.TrailerType, vehicle.OdometerKm, vehicle.DepotId, vehicle.Status, vehicle.Version,
            history, VehicleRules.Documents(vehicle.Kind, vehicle.GrossWeightKg));
    }

    public static async Task<VehicleDetail> ReadByConnectionAsync(string connectionString, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(connection).Options;
        await using var db = new FleetOpsDbContext(options);
        db.Database.UseTransaction(transaction);
        var result = await Read(db, id, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
