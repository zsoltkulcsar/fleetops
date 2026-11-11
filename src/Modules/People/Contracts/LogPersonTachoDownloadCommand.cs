using FleetOps.Kernel.Authorization;
namespace FleetOps.People;

[CommandPolicy("tacho.manage")]
public sealed record LogPersonTachoDownloadCommand(Guid Id, DateOnly Date) : ICommand<PersonTachoDownloadResult>;
