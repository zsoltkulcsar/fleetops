using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[CommandPolicy("people.roles", RequiresStepUp = true)]
public sealed record SetPersonRolesCommand(Guid PersonId, string[]? Roles, string? Reason) : ICommand<AccessActionResult>;
