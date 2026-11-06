using System.Reflection;

namespace FleetOps.Modules.Audit;

public static class AuditArchitectureRules
{
    public static IReadOnlyList<string> ValidateAssembly(Assembly assembly)
    {
        var errors = new List<string>();
        foreach (var type in assembly.GetTypes().Where(x => x.GetCustomAttribute<AuditedEntityAttribute>() is not null))
            foreach (var property in type.GetProperties().Where(x => x.GetCustomAttribute<AuditFieldAttribute>() is null))
                errors.Add($"{type.FullName}.{property.Name} has no audit field class");
        return errors;
    }
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class AuditedEntityAttribute : Attribute { }
