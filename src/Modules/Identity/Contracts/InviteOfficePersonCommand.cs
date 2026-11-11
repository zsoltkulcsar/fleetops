using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[CommandPolicy("people.invite")]
public sealed record InviteOfficePersonCommand(Guid PersonId) : ICommand<AccessActionResult>;
