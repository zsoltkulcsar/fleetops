namespace FleetOps.Modules.Audit;

[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditFieldAttribute(string classification) : Attribute
{
    public string Classification { get; } = classification;
}

public static class AuditFieldClasses
{
    public const string Plain = "plain";
    public const string Personal = "personal";
    public const string Special = "special";
    public const string Technical = "technical";
}
