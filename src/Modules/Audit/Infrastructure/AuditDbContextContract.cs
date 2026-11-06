namespace FleetOps.Modules.Audit;

public interface IAuditDbContext
{
    void AddAudit(AuditEntry entry);
    Guid AuditActorId { get; }
    string AuditReason { get; }
}
