namespace FleetOps.Kernel.Authorization;

using System.Data.Common;

public interface ICommand { }
public interface ICommand<TResult> : ICommand { }

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class CommandPolicyAttribute(string actionKey) : Attribute
{
    public string ActionKey { get; } = actionKey;
    public SeparationOfDuty Separation { get; init; } = SeparationOfDuty.None;
    public bool HasStatePrecondition { get; init; }
}

public interface IHasSubject { Guid? SubjectId { get; } }

public sealed class CommandContext(DbConnection connection, DbTransaction transaction)
{
    public DbConnection DbConnection { get; } = connection;
    public DbTransaction DbTransaction { get; } = transaction;
}

public interface ICommandHandler<in TCommand, TResult> where TCommand : ICommand<TResult>
{
    Task<TResult> HandleAsync(TCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}

public interface ICommandStatePrecondition<in TCommand> where TCommand : ICommand
{
    Task<bool> CheckAsync(TCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}

public enum SubjectRelation { Any, Own }
public enum SeparationOfDuty { None, NotOwnSubject, OwnerRoutesToHr }

public static class RefusalCodes
{
    public const string NotSignedIn = "not_signed_in";
    public const string PermissionDenied = "permission_denied";
    public const string SelfApproval = "self_approval";
    public const string OwnerRoutesToHr = "owner_routes_to_hr";
    public const string StateConflict = "state_conflict";
}

public interface ISubjectRoleLookup
{
    Task<IReadOnlySet<RoleCode>?> GetRolesAsync(Guid subjectId, CommandContext context, CancellationToken cancellationToken);
}

public sealed class FailClosedSubjectRoleLookup : ISubjectRoleLookup
{
    public Task<IReadOnlySet<RoleCode>?> GetRolesAsync(Guid subjectId, CommandContext context, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<RoleCode>?>(null);
}
