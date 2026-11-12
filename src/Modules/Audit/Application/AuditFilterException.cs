namespace FleetOps.Modules.Audit;
public sealed class AuditFilterException(IReadOnlyList<string> fields) : Exception("validation_error")
{
    public IReadOnlyList<string> Fields { get; } = fields;
}
