using FleetOps.People;
using Microsoft.EntityFrameworkCore;

namespace FleetOps.Migrations;

public sealed class EfPersonHistoryStore(IConfiguration configuration) : IPersonHistoryStore
{
    public async Task<PersonHistory> ReadAsync(Guid personId, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FleetOpsDbContext>();
        MigrationDbContextOptions.Configure(options, configuration.GetConnectionString("FleetOps") ?? configuration["FLEETOPS_DATABASE_RUNTIME_URL"] ?? throw new InvalidOperationException("Database is not configured."));
        await using var db = new FleetOpsDbContext(options.Options);
        if (!await db.Employees.AnyAsync(person => person.Id == personId, cancellationToken)) throw new PersonConflictException("not_found");
        var documents = db.Documents.Where(document => document.OwnerId == personId && document.OwnerType == FleetOps.Modules.Documents.DocumentOwnerType.Employee);
        var sessions = db.OfficeSessions.Where(session => session.EmployeeId == personId);
        var entries = await db.AuditEntries.AsNoTracking()
            .Where(entry => (entry.EntityId == personId && (entry.EntityType == "employee" || entry.EntityType == "Employee"))
                || ((entry.EntityType == "Document" || entry.EntityType == "document") && documents.Any(document => document.Id == entry.EntityId))
                || (entry.EntityType == "session" && sessions.Any(session => session.Id == entry.EntityId)))
            .OrderByDescending(entry => entry.CreatedAt).ThenByDescending(entry => entry.Id)
            .Select(entry => new PersonHistoryEntry(entry.Id, entry.CreatedAt, entry.ActorId, entry.FieldName,
                entry.OldValue, entry.NewValue, entry.Reason,
                documents.Any(document => document.Id == entry.EntityId && document.Type == FleetOps.Modules.Documents.DocumentType.Medical))).ToArrayAsync(cancellationToken);
        return new(entries);
    }
}
