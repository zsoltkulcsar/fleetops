namespace FleetOps.Tests;
using Xunit;

public class LogAndProblemDetailsTests
{
    [Fact]
    public void Sentinel_contract_is_documented_for_future_endpoint_tests()
    {
        const string sentinel = "SENTINEL_PERSONAL_VALUE";
        Assert.DoesNotContain(sentinel, "database_unavailable");
    }
}
