using FleetOps.Kernel.Authorization;
namespace FleetOps.People;
[CommandPolicy("people.manage")]
public sealed record SuspendPersonCommand(Guid? SubjectId, string? Reason) : ICommand<PersonSaveResult>, IHasSubject;
