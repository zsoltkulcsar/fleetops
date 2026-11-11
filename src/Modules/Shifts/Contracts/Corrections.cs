using FleetOps.Kernel.Authorization;
namespace FleetOps.Modules.Shifts;
public sealed record CorrectionPayload(Guid? ShiftId, DateOnly Date, string Reason, DateTimeOffset? ProposedStartAt = null, DateTimeOffset? ProposedEndAt = null, string? Note = null);
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record CorrectionFieldDiff(string Field, string? Before, string? After);
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record CorrectionView(Guid Id, Guid EmployeeId, Guid? ShiftId, DateOnly Date, string Reason,
    DateTimeOffset? ProposedStartAt, DateTimeOffset? ProposedEndAt, string? Note, string Status, bool RequiresHr,
    string? DeclineReason, IReadOnlyList<CorrectionFieldDiff> Diffs, DateTimeOffset RequestedAt, DateTimeOffset? DecidedAt);
[VisibleTo(RoleCode.Driver, RoleCode.Dispatcher, RoleCode.Hr, RoleCode.Owner)]
public sealed record CorrectionList(IReadOnlyList<CorrectionView> Requests) : IReadModel;
[QueryPolicy("shifts.enter_edit")]
public sealed record GetCorrectionsQuery(Guid? SubjectId = null) : IQuery<CorrectionList>, IHasSubject;
[CommandPolicy("shifts.enter_edit", HasStatePrecondition = true)]
public sealed record DecideCorrectionCommand(Guid RequestId, Guid? SubjectId, string Decision, string? Reason = null, Guid? ShiftId = null) : ICommand<CorrectionList>, IHasSubject;
public interface IDriverCorrections
{
    Task<DriverCommandResult> ApplyAsync(DriverCommand command, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
public interface ICorrectionStore
{
    Task<CorrectionRequest?> LoadAsync(Guid id, CommandContext context, CancellationToken cancellationToken);
    Task<bool> MissingWorkdayAsync(Guid employeeId, DateOnly date, CommandContext context, CancellationToken cancellationToken);
    Task<bool> HasOpenAsync(Guid employeeId, Guid? shiftId, DateOnly date, CommandContext context, CancellationToken cancellationToken);
    Task<IReadOnlyList<CorrectionView>> ListAsync(Guid? employeeId, CommandContext? context, CancellationToken cancellationToken);
    Task SaveAsync(CorrectionRequest request, AccessContext actor, bool decided, CommandContext context, CancellationToken cancellationToken);
}
