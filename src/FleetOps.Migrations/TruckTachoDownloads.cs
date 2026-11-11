using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Fleet;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;

public sealed class EfTruckTachoDownloads : ITruckTachoDownloads
{
    public async Task<Vehicle?> LockAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        return await db.Vehicles.FromSqlInterpolated($"SELECT * FROM fleetops.vehicles WHERE id = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(Vehicle truck, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        db.AuditActorId = actorId;
        db.AuditReason = "tacho_download_logged";
        var original = await db.Vehicles.SingleAsync(row => row.Id == truck.Id, cancellationToken);
        original.Version++;
        original.LastTachoDownloadAt = truck.LastTachoDownloadAt;
        await db.SaveChangesAsync(cancellationToken);
    }
}
