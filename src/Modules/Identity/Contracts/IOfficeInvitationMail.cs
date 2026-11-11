using FleetOps.Kernel.Authorization;
namespace FleetOps.Identity;
public interface IOfficeInvitationMail
{
    TimeSpan TokenLifetime { get; }
    void Send(string email, string token);
}
