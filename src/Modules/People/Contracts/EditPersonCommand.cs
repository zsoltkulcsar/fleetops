using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[CommandPolicy("people.manage")]
public sealed record EditPersonCommand(Guid? SubjectId, PersonFields Fields) : ICommand<PersonSaveResult>, IHasSubject;
