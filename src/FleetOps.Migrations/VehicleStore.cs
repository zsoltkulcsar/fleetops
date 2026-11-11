using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using FleetOps.Kernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetOps.Migrations;

public sealed class EfVehicleStore(TimeProvider? time = null, IChangedSubjects? changedSubjects = null) : IVehicleStore
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    public static async Task<VehicleListPage> ReadListByConnectionAsync(
        string connectionString,
        ListVehiclesQuery query,
        VehicleListCursor? cursor,
        CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new FleetOpsDbContext(options);
        var vehicles = db.Vehicles.AsNoTracking().AsQueryable();
        if (!query.IncludeRetired)
            vehicles = vehicles.Where(vehicle => vehicle.Status != VehicleStatus.Retired);
        if (query.Kind is not null)
            vehicles = vehicles.Where(vehicle => vehicle.Kind == Enum.Parse<VehicleKind>(query.Kind));
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
            vehicles = vehicles.Where(vehicle => EF.Functions.ILike(vehicle.Plate, $"%{search}%", "\\"));
        }
        if (cursor is not null)
            vehicles = vehicles.Where(vehicle => string.Compare(vehicle.Plate, cursor.Plate) > 0 || (vehicle.Plate == cursor.Plate && vehicle.Id.CompareTo(cursor.Id) > 0));
        var rows = await vehicles.OrderBy(vehicle => vehicle.Plate).ThenBy(vehicle => vehicle.Id).Take(query.Limit + 1)
            .Select(vehicle => new VehicleListItem(vehicle.Id, vehicle.Kind, vehicle.Plate, vehicle.Make, vehicle.Model, vehicle.Year, vehicle.Status, vehicle.Ownership, vehicle.Version, ReadinessShownStatus.Active, false, Array.Empty<ReadinessCause>()))
            .ToListAsync(cancellationToken);
        var hasNext = rows.Count > query.Limit;
        if (hasNext)
            rows.RemoveAt(rows.Count - 1);
        return new(rows, hasNext ? new VehicleListCursor(rows[^1].Plate, rows[^1].Id) : null);
    }

    public async Task<VehicleDetail> CreateAsync(CreateVehicleCommand input, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actor.EmployeeId, input.Reason);
        if (await db.Vehicles.AnyAsync(x => x.Status != VehicleStatus.Retired && (x.Plate == input.Plate || x.Vin == input.Vin), cancellationToken))
            throw new VehicleConflictException("duplicate_vehicle");
        var depot = await db.Depots.SingleAsync(cancellationToken);
        if (input.DepotId is Guid requestedDepot && requestedDepot != depot.Id) throw new VehicleValidationException(["depotId"]);
        var vehicle = new Vehicle { Kind = input.Kind, Plate = input.Plate, Vin = input.Vin, Make = input.Make, Model = input.Model,
            Year = input.Year, GrossWeightKg = input.GrossWeightKg, RequiredLicenceClass = input.RequiredLicenceClass, Ownership = input.Ownership,
            LeaseEndDate = input.LeaseEndDate, TrailerType = input.TrailerType, OdometerKm = input.OdometerKm, OdometerUpdatedAt = input.OdometerKm.HasValue ? clock.GetUtcNow() : null, DepotId = input.DepotId ?? depot.Id,
            ServiceIntervalKm = input.ServiceIntervalKm, ServiceIntervalMonths = input.ServiceIntervalMonths, LastTachoDownloadAt = input.LastTachoDownloadAt };
        db.Vehicles.Add(vehicle);
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { throw new VehicleConflictException("duplicate_vehicle"); }
        changedSubjects?.Add("vehicle", vehicle.Id);
        return await Read(db, vehicle.Id, cancellationToken);
    }

    public async Task<VehicleDetail> EditAsync(EditVehicleCommand input, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actor.EmployeeId, input.Reason);
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.Id == input.SubjectId, cancellationToken) ?? throw new VehicleConflictException("vehicle_not_found");
        if (vehicle.Version != input.Version) throw new VehicleConflictException("version_conflict");
        if (vehicle.Status == VehicleStatus.Retired) throw new VehicleConflictException("state_conflict");
        if (vehicle.Kind != input.Kind) throw new VehicleValidationException(["kind"]);
        if (input.DepotId is Guid requestedDepot && !await db.Depots.AnyAsync(x => x.Id == requestedDepot, cancellationToken)) throw new VehicleValidationException(["depotId"]);
        if (vehicle.Plate != input.Plate && string.IsNullOrWhiteSpace(input.Reason)) throw new VehicleValidationException(["reason"]);
        if (await db.Vehicles.AnyAsync(x => x.Id != vehicle.Id && x.Status != VehicleStatus.Retired && (x.Plate == input.Plate || x.Vin == input.Vin), cancellationToken))
            throw new VehicleConflictException("duplicate_vehicle");
        if (vehicle.Plate != input.Plate) db.PlateHistories.Add(new PlateHistory { VehicleId = vehicle.Id, Plate = vehicle.Plate });
        vehicle.Kind = input.Kind; vehicle.Plate = input.Plate; vehicle.Vin = input.Vin; vehicle.Make = input.Make; vehicle.Model = input.Model;
        vehicle.Year = input.Year; vehicle.GrossWeightKg = input.GrossWeightKg; vehicle.RequiredLicenceClass = input.RequiredLicenceClass;
        if (vehicle.OdometerKm != input.OdometerKm) vehicle.OdometerUpdatedAt = input.OdometerKm.HasValue ? clock.GetUtcNow() : null;
        vehicle.Ownership = input.Ownership; vehicle.LeaseEndDate = input.LeaseEndDate; vehicle.TrailerType = input.TrailerType; vehicle.OdometerKm = input.OdometerKm;
        vehicle.DepotId = input.DepotId ?? vehicle.DepotId; vehicle.ServiceIntervalKm = input.ServiceIntervalKm; vehicle.ServiceIntervalMonths = input.ServiceIntervalMonths;
        vehicle.LastTachoDownloadAt = input.LastTachoDownloadAt; vehicle.Version++; vehicle.UpdatedAt = DateTimeOffset.UtcNow;
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { throw new VehicleConflictException("version_conflict"); } catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { throw new VehicleConflictException("duplicate_vehicle"); }
        changedSubjects?.Add("vehicle", vehicle.Id);
        return await Read(db, vehicle.Id, cancellationToken);
    }

    public async Task<VehicleDetail> RetireAsync(RetireVehicleCommand input, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actor.EmployeeId, input.Reason);
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.Id == input.SubjectId, cancellationToken) ?? throw new VehicleConflictException("vehicle_not_found");
        if (vehicle.Version != input.Version) throw new VehicleConflictException("version_conflict");
        if (vehicle.Status == VehicleStatus.Retired) throw new VehicleConflictException("state_conflict");
        vehicle.Status = VehicleStatus.Retired;
        vehicle.RetiredReason = input.RetiredReason;
        vehicle.RetiredAt = clock.GetUtcNow();
        vehicle.Version++;
        vehicle.UpdatedAt = clock.GetUtcNow();
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { throw new VehicleConflictException("version_conflict"); }
        changedSubjects?.Add("vehicle", vehicle.Id);
        return await Read(db, vehicle.Id, cancellationToken);
    }

    public async Task<VehicleDetail> UndoRetireAsync(UndoRetireVehicleCommand input, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actor.EmployeeId, input.Reason);
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.Id == input.SubjectId, cancellationToken) ?? throw new VehicleConflictException("vehicle_not_found");
        if (vehicle.Version != input.Version) throw new VehicleConflictException("version_conflict");
        if (vehicle.Status != VehicleStatus.Retired || vehicle.RetiredAt is null) throw new VehicleConflictException("state_conflict");
        if (clock.GetUtcNow() - vehicle.RetiredAt.Value >= TimeSpan.FromDays(30)) throw new VehicleConflictException("undo_window_closed");
        if (await db.Vehicles.AnyAsync(x => x.Id != vehicle.Id && x.Status != VehicleStatus.Retired && (x.Plate == vehicle.Plate || x.Vin == vehicle.Vin), cancellationToken))
            throw new VehicleConflictException("plate_in_use");
        vehicle.Status = VehicleStatus.Active;
        vehicle.RetiredReason = null;
        vehicle.RetiredAt = null;
        vehicle.Version++;
        vehicle.UpdatedAt = clock.GetUtcNow();
        try { await db.SaveChangesAsync(cancellationToken); } catch (DbUpdateConcurrencyException) { throw new VehicleConflictException("version_conflict"); } catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" }) { throw new VehicleConflictException("plate_in_use"); }
        changedSubjects?.Add("vehicle", vehicle.Id);
        return await Read(db, vehicle.Id, cancellationToken);
    }

    public async Task<VehicleDetail> SetOffRoadAsync(SetVehicleOffRoadCommand input, AccessContext actor, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = CreateDb(context, actor.EmployeeId, input.Reason);
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.Id == input.SubjectId, cancellationToken) ?? throw new VehicleConflictException("vehicle_not_found");
        if (vehicle.Version != input.Version) throw new VehicleConflictException("version_conflict");
        if (vehicle.Status == VehicleStatus.Retired) throw new VehicleConflictException("state_conflict");
        vehicle.ManualOffRoad = input.ManualOffRoad;
        vehicle.Version++;
        vehicle.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        changedSubjects?.Add("vehicle", vehicle.Id);
        return await Read(db, vehicle.Id, cancellationToken);
    }

    private static FleetOpsDbContext CreateDb(CommandContext context, Guid actorId, string? reason)
    {
        var optionsBuilder = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.ConfigureRuntime(optionsBuilder, context.DbConnection);
        var options = optionsBuilder.Options;
        var db = new FleetOpsDbContext(options) { AuditActorId = actorId, AuditReason = reason };
        db.Database.UseTransaction(context.DbTransaction);
        return db;
    }

    internal static async Task<VehicleDetail> Read(FleetOpsDbContext db, Guid id, CancellationToken cancellationToken)
    {
        var vehicle = await db.Vehicles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw new VehicleConflictException("not_found");
        var history = await db.PlateHistories.Where(x => x.VehicleId == id).OrderBy(x => x.ChangedAt).Select(x => x.Plate).ToListAsync(cancellationToken);
        return new(vehicle.Id, vehicle.Kind, vehicle.Plate, vehicle.Vin, vehicle.Make, vehicle.Model, vehicle.Year, vehicle.GrossWeightKg,
            vehicle.Ownership, vehicle.LeaseEndDate, vehicle.TrailerType, vehicle.OdometerKm, vehicle.DepotId, vehicle.Status, vehicle.RetiredReason, vehicle.RetiredAt, vehicle.Version,
            vehicle.RequiredLicenceClass, vehicle.ServiceIntervalKm, vehicle.ServiceIntervalMonths, vehicle.LastTachoDownloadAt,
            history, VehicleRules.Documents(vehicle.Kind, vehicle.GrossWeightKg), vehicle.Status == VehicleStatus.Retired ? ReadinessShownStatus.Retired : ReadinessShownStatus.Active, false, []);
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
