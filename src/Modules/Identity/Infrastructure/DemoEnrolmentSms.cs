using Microsoft.Extensions.Options;
namespace FleetOps.Identity;
public sealed class DemoEnrolmentSms(IOptions<EnrolmentOptions> options) : IEnrolmentSms
{
    public Task SendAsync(string phone, string code, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!options.Value.TestInbox)
            throw new SmsUnavailableException();
        return Task.CompletedTask;
    }
}
