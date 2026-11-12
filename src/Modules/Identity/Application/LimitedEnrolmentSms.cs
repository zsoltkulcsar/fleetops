using FleetOps.Kernel;
using FleetOps.Modules.Setup;
namespace FleetOps.Identity;
public sealed class LimitedEnrolmentSms(ISmsDailyBudget budget, DemoEnrolmentSms sender, ISettings settings, ICompanyTimeService companyTime, TimeProvider time) : IEnrolmentSms
{
    public async Task SendAsync(string phone, string code, CancellationToken cancellationToken)
    {
        var limit = await settings.GetIntAsync(new("sms.daily_limit"), null, cancellationToken);
        if (limit <= 0 || !await budget.TryReserveAsync(companyTime.ToCompanyDate(time.GetUtcNow()), limit, cancellationToken)) throw new SmsLimitReachedException();
        await sender.SendAsync(phone, code, cancellationToken);
    }
}
