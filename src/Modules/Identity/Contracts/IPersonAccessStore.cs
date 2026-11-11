using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
public interface IPersonAccessStore
{
    Task<PersonSessions> SessionsAsync(Guid personId, CancellationToken cancellationToken);
    Task<AccessPerson?> LockPersonAsync(Guid personId, CommandContext context, CancellationToken cancellationToken);
    Task<int> OwnersAsync(CommandContext context, CancellationToken cancellationToken);
    Task RevokeAsync(Guid personId, Guid sessionId, string reason, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task ResetPinAsync(Guid personId, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task CompletePinResetAsync(Guid personId, Guid sessionId, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task<bool> PinResetRequiredAsync(Guid sessionId, CancellationToken cancellationToken);
    Task ChangePhoneAsync(Guid personId, string phone, string reason, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task SetRolesAsync(Guid personId, string[] roles, bool revokeOfficeSessions, string reason, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
    Task InviteAsync(AccessPerson person, AccessContext actor, CommandContext context, CancellationToken cancellationToken);
}
