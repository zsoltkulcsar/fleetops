using FleetOps.Api.Pipeline;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace FleetOps.Tests;

public sealed class DatabasePoolLimitsTests
{
    [Fact]
    public void Runtime_connection_string_gets_a_pool_cap_and_idle_lifetime()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FleetOps"] = "Host=db.example;Database=fleetops;Username=runtime",
            ["FLEETOPS_DATABASE_RUNTIME_URL"] = "Host=db.example;Database=fleetops;Username=runtime;Maximum Pool Size=4",
        }).Build();

        DatabasePoolLimits.Apply(configuration);

        var primary = new NpgsqlConnectionStringBuilder(configuration["ConnectionStrings:FleetOps"]);
        Assert.Equal(10, primary.MaxPoolSize);
        Assert.Equal(60, primary.ConnectionIdleLifetime);
        Assert.Equal("db.example", primary.Host);
        // A string that already names its pool size keeps it.
        Assert.Equal(4, new NpgsqlConnectionStringBuilder(configuration["FLEETOPS_DATABASE_RUNTIME_URL"]).MaxPoolSize);
    }

    [Fact]
    public void Pool_cap_comes_from_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:FleetOps"] = "Host=db.example;Database=fleetops;Username=runtime",
            ["Database:MaxPoolSize"] = "6",
        }).Build();

        DatabasePoolLimits.Apply(configuration);

        Assert.Equal(6, new NpgsqlConnectionStringBuilder(configuration["ConnectionStrings:FleetOps"]).MaxPoolSize);
    }
}
