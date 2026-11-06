namespace FleetOps.Modules.Audit;

public sealed class AuditEntry
{
    public Guid Id { get; set; }
    public string EntityType { get; set; } = "";
    public Guid EntityId { get; set; }
    public string FieldName { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
