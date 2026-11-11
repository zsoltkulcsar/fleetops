using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[CommandPolicy("people.manage")]
public sealed record ActivatePersonCommand(Guid? SubjectId, bool AcceptWarning) : ICommand<PersonSaveResult>, IHasSubject;
