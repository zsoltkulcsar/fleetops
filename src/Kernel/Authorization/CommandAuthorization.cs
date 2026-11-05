namespace FleetOps.Kernel.Authorization;

public sealed class AuthorizationException(string code, string actionKey, IReadOnlyCollection<RoleCode> allowedRoles) : Exception(code)
{
    public string Code { get; } = code;
    public string ActionKey { get; } = actionKey;
    public IReadOnlyCollection<RoleCode> AllowedRoles { get; } = allowedRoles;
}

public sealed class DefaultCommandPolicyProvider : ICommandPolicyProvider
{
    public CommandPolicy? GetPolicy(Type commandType)
    {
        var attribute = commandType.GetCustomAttributes(typeof(CommandPolicyAttribute), true).SingleOrDefault() as CommandPolicyAttribute;
        if (attribute is null) return null;
        var entry = PermissionMatrix.Resolve(attribute.ActionKey, Enum.GetValues<RoleCode>())?.Entry
            ?? throw new InvalidOperationException($"Unknown permission matrix key: {attribute.ActionKey}");
        if (entry.RequiredSeparation != attribute.Separation)
            throw new InvalidOperationException($"Command {commandType.FullName} must declare {entry.RequiredSeparation}.");
        if (entry.RequiresStatePrecondition && !attribute.HasStatePrecondition)
            throw new InvalidOperationException($"Command {commandType.FullName} must declare a state precondition.");
        var own = entry.OwnRoles;
        var relation = own.Count == 0 ? SubjectRelation.Any : SubjectRelation.Own;
        Func<ICommand, Guid?>? subject = relation == SubjectRelation.Own || attribute.Separation != SeparationOfDuty.None
            ? c => (c as IHasSubject)?.SubjectId : null;
        return new CommandPolicy(attribute.ActionKey, entry.AllowedRoles, relation,
            separation: attribute.Separation, subjectId: subject, ownRoles: own);
    }
}

public static class CommandArchitectureRules
{
    public static IReadOnlyList<string> ValidateAssembly(System.Reflection.Assembly assembly)
    {
        var errors = new List<string>();
        foreach (var type in assembly.GetTypes().Where(type => !type.IsAbstract && !type.IsInterface && typeof(ICommand).IsAssignableFrom(type)))
        {
            var attribute = type.GetCustomAttributes(typeof(CommandPolicyAttribute), true).SingleOrDefault() as CommandPolicyAttribute;
            if (attribute is null)
            {
                errors.Add($"Command {type.FullName} has no policy");
                continue;
            }
            var entry = PermissionMatrix.Entries.SingleOrDefault(x => x.ActionKey == attribute.ActionKey);
            if (entry is null) errors.Add($"Command {type.FullName} has unknown policy {attribute.ActionKey}");
            else
            {
                if (entry.RequiredSeparation != attribute.Separation)
                    errors.Add($"Command {type.FullName} omits required {entry.RequiredSeparation}");
                if (entry.RequiresStatePrecondition && !attribute.HasStatePrecondition)
                    errors.Add($"Command {type.FullName} omits required state precondition");
            }
            if ((entry?.OwnRoles.Count > 0 || attribute.Separation != SeparationOfDuty.None) && !typeof(IHasSubject).IsAssignableFrom(type))
                errors.Add($"Command {type.FullName} has subject-dependent policy but no IHasSubject");
        }
        return errors;
    }
}
