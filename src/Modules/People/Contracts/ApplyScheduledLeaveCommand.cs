using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
[CommandPolicy("people.manage")]
public sealed record ApplyScheduledLeaveCommand(Guid? SubjectId, DateOnly Today) : ICommand<PersonSaveResult>, IHasSubject;
