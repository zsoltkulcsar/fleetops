using FleetOps.Kernel;
using Xunit;

namespace FleetOps.Tests;

public class CompanyTimeServiceTests
{
    [Fact]
    public void Converts_utc_across_berlin_dst_boundary()
    {
        var service = new CompanyTimeService(CompanyTimeService.Resolve("Europe/Berlin"));
        Assert.Equal(new DateOnly(2026, 3, 29), service.ToCompanyDate(new DateTimeOffset(2026, 3, 28, 23, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 3, 30), service.ToCompanyDate(new DateTimeOffset(2026, 3, 29, 22, 30, 0, TimeSpan.Zero)));
    }
}
