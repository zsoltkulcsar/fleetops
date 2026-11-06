namespace FleetOps.Modules.Audit;

[AttributeUsage(AttributeTargets.Property)]
public sealed class AuditFieldAttribute(string classification) : Attribute
{
    public string Classification { get; } = classification;
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class NotAuditedAttribute : Attribute { }

public static class AuditFieldClasses
{
    public const string Plain = "plain";
    public const string Personal = "personal";
    public const string Special = "special";
}
