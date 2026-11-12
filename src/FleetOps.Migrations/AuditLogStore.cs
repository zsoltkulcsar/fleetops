using System.Reflection;
using FleetOps.Kernel.Authorization;
using FleetOps.Modules.Audit;
using FleetOps.Modules.Documents;
using Microsoft.EntityFrameworkCore;
namespace FleetOps.Migrations;
public sealed class AuditLogStore(IConfiguration configuration, TimeProvider time) : IAuditLogStore
{
    public async Task<AuditSourcePage> ReadAsync(AuditFilter filter, CommandContext? context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        var entries = db.AuditEntries.AsNoTracking().AsQueryable();
        if (filter.PersonId is Guid personId)
            entries = entries.Where(entry => entry.ActorId == personId.ToString() || entry.EntityId == personId ||
                db.Documents.Any(document => document.Id == entry.EntityId && document.OwnerType == DocumentOwnerType.Employee && document.OwnerId == personId) ||
                db.Shifts.Any(shift => shift.Id == entry.EntityId && shift.EmployeeId == personId));
        if (filter.EntityType is not null) entries = entries.Where(entry => entry.EntityType == filter.EntityType);
        if (filter.EntityId is Guid entityId) entries = entries.Where(entry => entry.EntityId == entityId);
        if (filter.From is not null) entries = entries.Where(entry => entry.CreatedAt >= filter.From);
        if (filter.To is not null) entries = entries.Where(entry => entry.CreatedAt <= filter.To);
        var total = await entries.CountAsync(cancellationToken);
        var rows = await entries.OrderByDescending(entry => entry.CreatedAt).ThenByDescending(entry => entry.Id)
            .Skip((int)Math.Min((filter.Page - 1L) * filter.PageSize, int.MaxValue)).Take(filter.PageSize).ToListAsync(cancellationToken);
        var documentIds = rows.Where(entry => entry.EntityType.ToLower() == "document").Select(entry => entry.EntityId).ToArray();
        var nonMedicalIds = await db.Documents.Where(document => documentIds.Contains(document.Id) && document.Type != DocumentType.Medical).Select(document => document.Id).ToListAsync(cancellationToken);
        var entityTypes = db.Model.GetEntityTypes().Select(entity => entity.ClrType).Distinct().ToDictionary(type => type.Name, StringComparer.OrdinalIgnoreCase);
        // Show people by name, not by ID; actors that are not a person (system jobs) keep their label.
        var actorIds = rows.Select(entry => Guid.TryParse(entry.ActorId, out var actorId) ? actorId : Guid.Empty).Where(actorId => actorId != Guid.Empty).Distinct().ToArray();
        var actorNames = await db.Employees.AsNoTracking().Where(employee => actorIds.Contains(employee.Id))
            .ToDictionaryAsync(employee => employee.Id.ToString(), employee => employee.FirstName + " " + employee.LastName, cancellationToken);
        return new(rows.Select(entry =>
        {
            var special = (entry.EntityType.ToLower() == "document" && !nonMedicalIds.Contains(entry.EntityId)) || entry.FieldName.Contains("phone", StringComparison.OrdinalIgnoreCase) || entry.FieldName.Contains("pin", StringComparison.OrdinalIgnoreCase) ||
                entityTypes.TryGetValue(entry.EntityType, out var type) && type.GetProperty(entry.FieldName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetCustomAttribute<AuditFieldAttribute>()?.Classification == AuditFieldClasses.Special;
            return new AuditSourceRow(new AuditRow(entry.Id, entry.CreatedAt, actorNames.GetValueOrDefault(entry.ActorId, entry.ActorId), entry.EntityType, entry.EntityId, entry.FieldName, entry.OldValue, entry.NewValue, entry.Reason), special);
        }).ToArray(), total, filter.Page, filter.PageSize);
    }
    public async Task RecordExportAsync(Guid actorId, CommandContext context, CancellationToken cancellationToken)
    {
        await using var db = NotificationDb.Create(configuration, context);
        db.AuditEntries.Add(new AuditEntry { Id = Guid.CreateVersion7(), EntityType = "AuditLog", EntityId = Guid.CreateVersion7(), FieldName = "export", ActorId = actorId.ToString(), Reason = "CSV export", CreatedAt = time.GetUtcNow() });
        await db.SaveChangesAsync(cancellationToken);
    }
}


