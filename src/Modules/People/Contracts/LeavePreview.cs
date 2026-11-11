using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
[VisibleTo(RoleCode.Hr, RoleCode.Owner)]
public sealed record LeavePreview(Guid EmployeeId, DateOnly EndDate, IReadOnlyList<Guid> SessionsToRevoke,
    IReadOnlyList<Guid> AssignmentsToEnd, IReadOnlyList<Guid> AssignmentsToRemove, IReadOnlyList<Guid> CyclesToEnd,
    IReadOnlyList<Guid> CyclesToRemove, IReadOnlyList<Guid> AlertsToResolve, Guid? RunningShiftId,
    int? RunningShiftVersion, IReadOnlyList<Guid> ShiftsToApprove) : IReadModel;
