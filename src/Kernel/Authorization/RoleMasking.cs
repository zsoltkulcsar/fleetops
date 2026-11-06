namespace FleetOps.Kernel.Authorization;

public interface IQuery<TResult> { }

public interface IQueryHandler<in TQuery, TResult> where TQuery : IQuery<TResult>
{
    Task<TResult> HandleAsync(TQuery query, AccessContext actor, CancellationToken cancellationToken);
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class MedicalContentAttribute : Attribute { }

public interface IOutboundPayload { }

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class QueryPolicyAttribute(string actionKey) : Attribute { public string ActionKey { get; } = actionKey; }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
public sealed class VisibleToAttribute(params RoleCode[] roles) : Attribute
{
    public IReadOnlySet<RoleCode> Roles { get; } = roles.ToHashSet();
}

public interface IReadModel { }

public static class RoleMaskingInspector
{
    public static IReadOnlyList<string> ValidateDispatcherOutput(Type outputType)
    {
        var errors = new List<string>();
        var visited = new HashSet<Type>();
        Walk(outputType, outputType.FullName ?? outputType.Name, errors, visited);
        return errors;
    }

    private static void Walk(Type type, string path, List<string> errors, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsScalar(type)) return;
        if (IsDomainType(type)) { errors.Add($"{path} contains Domain type {type.FullName}"); return; }
        if (type == typeof(object))
        {
            errors.Add($"{path} has uninspectable type System.Object");
            return;
        }
        if (type.IsArray || IsEnumerable(type))
        {
            if (!visited.Add(type)) return;
            var elements = type.IsArray
                ? [type.GetElementType()!]
                : type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>)).Select(i => i.GetGenericArguments()[0]).Distinct();
            if (!elements.Any()) errors.Add($"{path} is a non-generic collection");
            foreach (var element in elements) Walk(element, path + "[]", errors, visited);
            return;
        }
        if (type.IsInterface || type.IsAbstract)
        {
            errors.Add($"{path} has uninspectable type {type.FullName ?? type.Name}");
            return;
        }
        if (!visited.Add(type)) return;
        if (type.GetCustomAttributes(typeof(MedicalContentAttribute), true).Length > 0)
            errors.Add($"{path} is medical content");
        var visibleTo = type.GetCustomAttributes(typeof(VisibleToAttribute), true).SingleOrDefault() as VisibleToAttribute;
        if (visibleTo is null)
            errors.Add($"{path} has no VisibleTo attribute");
        else if (path.Contains('.', StringComparison.Ordinal) && !visibleTo.Roles.Contains(RoleCode.Dispatcher))
            errors.Add($"{path} is not visible to dispatcher");
        else if (visibleTo.Roles.Contains(RoleCode.Dispatcher) && type.GetCustomAttributes(typeof(MedicalContentAttribute), true).Length > 0)
            errors.Add($"{path} exposes medical content to dispatcher");

        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                Walk(argument, path + "[]", errors, visited);
        }

        foreach (var member in type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
        {
            Type? memberType = member switch
            {
                System.Reflection.PropertyInfo property when property.GetIndexParameters().Length == 0 => property.PropertyType,
                System.Reflection.FieldInfo field => field.FieldType,
                _ => null
            };
            if (memberType is null || (!member.IsPublicMember() && member.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonIncludeAttribute), true).Length == 0)) continue;
            var memberPath = path + "." + member.Name;
            if (member.GetCustomAttributes(typeof(MedicalContentAttribute), true).Length > 0)
            {
                var declaringName = member.DeclaringType?.Name ?? type.Name;
                errors.Add($"{path} -> {declaringName}.{member.Name} is medical content");
            }
            Walk(memberType, memberPath, errors, visited);
        }
    }

    private static bool IsEnumerable(Type type) => typeof(System.Collections.IEnumerable).IsAssignableFrom(type);

    public static IReadOnlyList<string> ValidateAssembly(System.Reflection.Assembly assembly)
    {
        var errors = new List<string>();
        var types = assembly.GetTypes();
        foreach (var type in types.Where(t => !t.IsAbstract && !t.IsInterface && (typeof(IReadModel).IsAssignableFrom(t) || typeof(IOutboundPayload).IsAssignableFrom(t))))
        {
            var visibleTo = type.GetCustomAttributes(typeof(VisibleToAttribute), false).SingleOrDefault() as VisibleToAttribute;
            if (visibleTo is null)
                errors.Add($"{type.FullName} has no VisibleTo attribute");
            else if (visibleTo.Roles.Contains(RoleCode.Dispatcher))
                errors.AddRange(ValidateDispatcherOutput(type));
        }

        foreach (var type in types)
        {
            foreach (var result in type.GetInterfaces()
                         .Where(i => i.IsGenericType &&
                            (i.GetGenericTypeDefinition() == typeof(IQuery<>) || i.GetGenericTypeDefinition() == typeof(ICommand<>)))
                         .Select(i => i.GetGenericArguments()[0])
                         .Where(result => !IsScalar(result)))
            {
                if (result.GetCustomAttributes(typeof(VisibleToAttribute), true).Length == 0)
                    errors.Add($"{type.FullName} result {result.FullName} has no VisibleTo attribute");
                else if (((VisibleToAttribute)result.GetCustomAttributes(typeof(VisibleToAttribute), true).Single()).Roles.Contains(RoleCode.Dispatcher))
                    errors.AddRange(ValidateDispatcherOutput(result));
            }
        }
        return errors;
    }

    public static IReadOnlyList<string> ValidateDomainTypes(Type outputType)
    {
        var errors = new List<string>();
        WalkDomain(outputType, outputType.FullName ?? outputType.Name, errors, new HashSet<Type>());
        return errors;
    }

    private static void WalkDomain(Type type, string path, List<string> errors, HashSet<Type> visited)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (IsScalar(type)) return;
        if (IsDomainType(type)) { errors.Add($"{path} -> {type.FullName ?? type.Name}"); return; }
        if (type == typeof(object) || type.IsInterface || type.IsAbstract) return;
        if (!visited.Add(type)) return;
        if (type.IsArray)
        {
            WalkDomain(type.GetElementType()!, path + "[]", errors, visited);
            return;
        }
        if (IsEnumerable(type))
        {
            foreach (var element in type.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                         .Select(i => i.GetGenericArguments()[0]).Distinct())
                WalkDomain(element, path + "[]", errors, visited);
            return;
        }
        if (type.IsGenericType)
            foreach (var argument in type.GetGenericArguments()) WalkDomain(argument, path + "[]", errors, visited);
        foreach (var member in type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            var memberType = member switch
            {
                System.Reflection.PropertyInfo property when property.GetIndexParameters().Length == 0 => property.PropertyType,
                System.Reflection.FieldInfo field => field.FieldType,
                _ => null
            };
            if (memberType is not null) WalkDomain(memberType, path + "." + member.Name, errors, visited);
        }
    }

    private static bool IsScalar(Type type) =>
        type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal) ||
        type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(DateOnly) || type == typeof(TimeOnly) || type == typeof(TimeSpan);

    private static bool IsDomainType(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type).Namespace is { } ns &&
        (ns.Equals("Domain", StringComparison.OrdinalIgnoreCase) ||
         ns.StartsWith("Domain.", StringComparison.OrdinalIgnoreCase) ||
         ns.Contains(".Domain.", StringComparison.OrdinalIgnoreCase) ||
         ns.EndsWith(".Domain", StringComparison.OrdinalIgnoreCase));
}

file static class ReflectionMemberExtensions
{
    public static bool IsPublicMember(this System.Reflection.MemberInfo member) => member switch
    {
        System.Reflection.PropertyInfo p => (p.GetMethod ?? p.SetMethod)?.IsPublic == true,
        System.Reflection.FieldInfo f => f.IsPublic,
        _ => false
    };
}

public static class QueryArchitectureRules
{
    public static IReadOnlyList<string> ValidateAssembly(System.Reflection.Assembly assembly)
    {
        var errors = new List<string>();
        foreach (var query in assembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface && ImplementsQuery(t)))
        {
            var results = query.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)).Select(i => i.GetGenericArguments()[0]).ToArray();
            foreach (var result in results)
            {
                if (!typeof(IReadModel).IsAssignableFrom(result))
                    errors.Add($"Query {query.FullName} does not return an IReadModel: {result.FullName}");
                errors.AddRange(RoleMaskingInspector.ValidateDomainTypes(result)
                    .Select(path => $"Query {query.FullName} output {path} contains a Domain type"));
            }
            var policy = query.GetCustomAttributes(typeof(QueryPolicyAttribute), true).SingleOrDefault() as QueryPolicyAttribute;
            if (policy is null) errors.Add($"Query {query.FullName} has no policy");
            else if (!PermissionMatrix.Entries.Any(x => x.ActionKey == policy.ActionKey)) errors.Add($"Query {query.FullName} has unknown policy {policy.ActionKey}");
        }

        foreach (var handler in assembly.GetTypes().Where(t => !t.IsAbstract && !t.IsInterface && LooksLikeQueryHandler(t)))
        {
            var methods = handler.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
                .Where(m => m.Name == "HandleAsync");
            if (methods.Any(m => !m.GetParameters().Any(p => p.ParameterType == typeof(AccessContext))))
                errors.Add($"Query handler {handler.FullName} has no AccessContext parameter");
        }

        return errors;
    }

    private static bool ImplementsQuery(Type type) => type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));
    private static bool LooksLikeQueryHandler(Type type) =>
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)) ||
        type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Any(m => m.Name == "HandleAsync" && m.GetParameters().FirstOrDefault()?.ParameterType.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>)) == true);
    private static bool IsDomainType(Type type) =>
        (Nullable.GetUnderlyingType(type) ?? type).Namespace is { } ns &&
            (ns.Equals("Domain", StringComparison.OrdinalIgnoreCase) ||
             ns.StartsWith("Domain.", StringComparison.OrdinalIgnoreCase) ||
             ns.Contains(".Domain.", StringComparison.OrdinalIgnoreCase) ||
             ns.EndsWith(".Domain", StringComparison.OrdinalIgnoreCase));
}
