namespace FleetOps.Kernel.Authorization;

public sealed record CommandPolicy
{
    public CommandPolicy(string actionKey, IReadOnlySet<RoleCode> allowedRoles,
        SubjectRelation subjectRelation = SubjectRelation.Any,
        Func<ICommand, AccessContext, bool>? statePrecondition = null,
        SeparationOfDuty separation = SeparationOfDuty.None,
        Func<ICommand, Guid?>? subjectId = null,
        IReadOnlySet<RoleCode>? ownRoles = null,
        bool requiresStepUp = false)
    {
        ActionKey = actionKey;
        AllowedRoles = allowedRoles;
        SubjectRelation = subjectRelation;
        StatePrecondition = statePrecondition;
        Separation = separation;
        SubjectId = subjectId;
        OwnRoles = ownRoles ?? new HashSet<RoleCode>();
        RequiresStepUp = requiresStepUp;
        if (string.IsNullOrWhiteSpace(ActionKey)) throw new ArgumentException("An action key is required.", nameof(actionKey));
        if (AllowedRoles is null || AllowedRoles.Count == 0) throw new ArgumentException("At least one allowed role is required.", nameof(allowedRoles));
        if ((SubjectRelation == SubjectRelation.Own || Separation != SeparationOfDuty.None) && SubjectId is null)
            throw new ArgumentException("A subject delegate is required for subject-dependent authorization.", nameof(subjectId));
        if (SubjectRelation == SubjectRelation.Own && OwnRoles.Count == 0)
            throw new ArgumentException("Own authorization requires at least one own role.", nameof(ownRoles));
        if (!OwnRoles.IsSubsetOf(AllowedRoles)) throw new ArgumentException("Own roles must be allowed roles.", nameof(ownRoles));
        if (OwnRoles.Count > 0 && SubjectRelation != SubjectRelation.Own)
            throw new ArgumentException("Own roles require Own subject relation.", nameof(ownRoles));
    }

    public string ActionKey { get; }
    public IReadOnlySet<RoleCode> AllowedRoles { get; }
    public SubjectRelation SubjectRelation { get; }
    public Func<ICommand, AccessContext, bool>? StatePrecondition { get; }
    public SeparationOfDuty Separation { get; }
    public Func<ICommand, Guid?>? SubjectId { get; }
    public IReadOnlySet<RoleCode> OwnRoles { get; }
    public bool RequiresStepUp { get; }
    public static CommandPolicy For(string actionKey, params RoleCode[] allowedRoles) => new(actionKey, allowedRoles.ToHashSet());
}

public interface ICommandPolicyProvider
{
    CommandPolicy? GetPolicy(Type commandType);
}
