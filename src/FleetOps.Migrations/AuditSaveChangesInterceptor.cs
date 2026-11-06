using FleetOps.Modules.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Reflection;

namespace FleetOps.Migrations;

public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) { Write(eventData.Context); return result; }
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) { Write(eventData.Context); return ValueTask.FromResult(result); }
    private static void Write(DbContext? context)
    {
        if (context is not IAuditDbContext audited) return;
        foreach (var entry in context.ChangeTracker.Entries().Where(x => x.Entity is not AuditEntry && x.Entity.GetType().GetCustomAttribute<AuditedEntityAttribute>() is not null && x.State == EntityState.Modified).ToArray())
        foreach (var property in entry.Properties.Where(x => x.Metadata.PropertyInfo?.GetCustomAttribute<AuditFieldAttribute>()?.Classification != AuditFieldClasses.Technical))
        {
            if (entry.State == EntityState.Modified && !property.IsModified) continue;
            audited.AddAudit(new AuditEntry { Id = Guid.CreateVersion7(), EntityType = entry.Metadata.ClrType.Name, EntityId = entry.Property("Id").CurrentValue is Guid id ? id : Guid.Empty, FieldName = property.Metadata.Name, ActorId = audited.AuditActorId.ToString(), Reason = audited.AuditReason, CreatedAt = DateTimeOffset.UtcNow });
        }
    }
}
