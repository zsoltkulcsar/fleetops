namespace FleetOps.Identity;

public interface IEnrolmentSms
{
    Task SendAsync(string phone, string code, CancellationToken cancellationToken);
}
