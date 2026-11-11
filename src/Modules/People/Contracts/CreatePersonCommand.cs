using FleetOps.Kernel.Authorization;

namespace FleetOps.People;

[CommandPolicy("people.manage")]
public sealed record CreatePersonCommand(PersonFields Fields, string[]? Roles) : ICommand<PersonSaveResult>;
