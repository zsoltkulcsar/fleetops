using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
[CommandPolicy("people.manage")]
public sealed record LeavePersonCommand(Guid? SubjectId, DateOnly? EndDate, string? Reason, DateTimeOffset? RunningShiftEnd = null, string? RunningShiftReason = null) : ICommand<PersonSaveResult>, IHasSubject;
