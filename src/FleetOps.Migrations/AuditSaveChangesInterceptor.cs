using FleetOps.Modules.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Reflection;

namespace FleetOps.Migrations;

public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Write(eventData.Context); return ValueTask.FromResult(result); }
    private static void Write(DbContext? context)
    {
        if (context is not IAuditDbContext audited) return;
        foreach (var entry in context.ChangeTracker.Entries().Where(x => x.Entity is not AuditEntry && x.Entity.GetType().GetCustomAttribute<AuditedEntityAttribute>() is not null && x.State is EntityState.Added or EntityState.Modified).ToArray())
        foreach (var property in entry.Properties.Where(x => x.Metadata.PropertyInfo?.GetCustomAttribute<AuditFieldAttribute>() is not null))
        {
            var classification = property.Metadata.PropertyInfo!.GetCustomAttribute<AuditFieldAttribute>()!.Classification;
            if (entry.State == EntityState.Modified && !property.IsModified) continue;
            if (entry.State == EntityState.Added && property.CurrentValue is null) continue;
            if (audited.AuditActorId == Guid.Empty) throw new InvalidOperationException("audit_actor_required");
            var oldValue = entry.State == EntityState.Modified ? Format(property.OriginalValue) : null;
            var newValue = Format(property.CurrentValue);
            if (entry.State == EntityState.Modified && classification == AuditFieldClasses.Special) { oldValue = null; newValue = null; }
            audited.AddAudit(new AuditEntry { Id = Guid.CreateVersion7(), EntityType = entry.Metadata.ClrType.Name, EntityId = entry.Property("Id").CurrentValue is Guid id ? id : throw new InvalidOperationException("audit_entity_id_required"), FieldName = property.Metadata.Name, OldValue = oldValue, NewValue = newValue, ActorId = audited.AuditActorId.ToString(), Reason = audited.AuditReason, CreatedAt = DateTimeOffset.UtcNow });
        }
    }
    private static string? Format(object? value) => value switch { null => null, DateOnly date => date.ToString("O"), DateTimeOffset time => time.ToString("O"), Enum e => e.ToString(), _ => value.ToString() };
}
