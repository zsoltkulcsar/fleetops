namespace FleetOps.Kernel;

public interface ICompanyTimeService
{
    DateTimeOffset ToCompanyTime(DateTimeOffset utcInstant);
    DateOnly ToCompanyDate(DateTimeOffset utcInstant);
}

public sealed class CompanyTimeService : ICompanyTimeService
{
    private readonly TimeZoneInfo _timezone;
    public CompanyTimeService(TimeZoneInfo timezone) => _timezone = timezone;

    public DateTimeOffset ToCompanyTime(DateTimeOffset utcInstant) =>
        TimeZoneInfo.ConvertTime(utcInstant.ToUniversalTime(), _timezone);

    public DateOnly ToCompanyDate(DateTimeOffset utcInstant) =>
        DateOnly.FromDateTime(ToCompanyTime(utcInstant).DateTime);

    public static TimeZoneInfo Resolve(string timezone) =>
        TimeZoneInfo.FindSystemTimeZoneById(timezone switch
        {
            "Europe/Berlin" when OperatingSystem.IsWindows() => "W. Europe Standard Time",
            _ => timezone
        });
}
