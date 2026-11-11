using FleetOps.Kernel.Authorization;
using FleetOps.People;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;

public sealed class EfPersonTachoDownloads(TimeProvider time) : IPersonTachoDownloads
{
    public async Task<Employee?> LockAsync(Guid id, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        return await db.Employees.FromSqlInterpolated($"SELECT * FROM fleetops.employees WHERE id = {id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SaveAsync(Employee person, Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = AlertEvaluatorDb.CreateDb(context);
        db.AuditActorId = actorId;
        db.AuditReason = "tacho_download_logged";
        var original = await db.Employees.SingleAsync(row => row.Id == person.Id, cancellationToken);
        db.AuditEntries.Add(new FleetOps.Modules.Audit.AuditEntry
        {
            Id = Guid.CreateVersion7(), EntityType = "employee", EntityId = person.Id,
            FieldName = "lastTachoDownloadAt", OldValue = original.LastTachoDownloadAt?.ToString("yyyy-MM-dd"),
            NewValue = person.LastTachoDownloadAt?.ToString("yyyy-MM-dd"), ActorId = actorId.ToString(),
            Reason = "tacho_download_logged", CreatedAt = time.GetUtcNow()
        });
        original.LastTachoDownloadAt = person.LastTachoDownloadAt;
        await db.SaveChangesAsync(cancellationToken);
    }
}
