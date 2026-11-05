using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FleetOps.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_is_degraded_without_a_database_and_does_not_disclose_connection_details()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        Assert.Equal(503, (int)response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Contains("database_unavailable", body);
        Assert.DoesNotContain("Host=", body);
        Assert.DoesNotContain("Password", body);
    }
}
