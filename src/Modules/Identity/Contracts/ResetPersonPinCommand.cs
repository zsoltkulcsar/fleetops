using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[CommandPolicy("sessions.pin_reset")]
public sealed record ResetPersonPinCommand(Guid PersonId) : ICommand<AccessActionResult>;
