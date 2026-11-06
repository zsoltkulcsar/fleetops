using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace FleetOps.Tests;

public class HealthTests
{
    [Fact]
    public async Task Health_is_ok_with_a_real_postgres_17_database()
    {
        await using var postgres = await TestPostgres.StartAsync(nameof(HealthTests), TestContext.Current.CancellationToken);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:FleetOps"] = postgres.ConnectionString })));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/healthz", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.Equal(200, (int)response.StatusCode);
        Assert.Contains("\"api\":\"ok\"", body);
        Assert.Contains("\"database\":\"ok\"", body);
    }

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
