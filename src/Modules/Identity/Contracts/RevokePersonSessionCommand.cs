using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
[CommandPolicy("sessions.revoke")]
public sealed record RevokePersonSessionCommand(Guid PersonId, Guid SessionId, string? Reason) : ICommand<AccessActionResult>;
