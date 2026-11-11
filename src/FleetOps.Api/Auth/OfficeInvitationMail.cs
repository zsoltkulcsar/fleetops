using FleetOps.Identity;
using Microsoft.Extensions.Options;
namespace FleetOps.Api.Auth;
public sealed class OfficeInvitationMail(IMailbox mailbox, IOptions<AuthOptions> options) : IOfficeInvitationMail
{
    public TimeSpan TokenLifetime => options.Value.ResetTokenLifetime;
    public void Send(string email, string token) => mailbox.Store(email, token);
}
