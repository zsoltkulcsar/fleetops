namespace FleetOps.Identity;
public sealed class SmsDailyUsage
{
    public DateOnly Date { get; set; }
    public int Count { get; set; }
}
public interface ISmsDailyBudget
{
    Task<bool> TryReserveAsync(DateOnly date, int limit, CancellationToken cancellationToken);
}
public sealed class SmsLimitReachedException() : Exception("sms_limit_reached");
